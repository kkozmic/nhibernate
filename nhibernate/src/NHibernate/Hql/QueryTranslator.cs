using System;
using System.Collections;
using System.Collections.Specialized;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

using NHibernate;
using NHibernate.Collection;
using NHibernate.Engine;
using NHibernate.Impl;
using NHibernate.Loader;
using NHibernate.Persister;
using NHibernate.Sql;
using NHibernate.SqlCommand;
using NHibernate.Type;
using NHibernate.Util;

namespace NHibernate.Hql 
{
	/// <summary> 
	/// An instance of <c>QueryTranslator</c> translates a Hibernate query string to SQL.
	/// </summary>
	public class QueryTranslator : Loader.Loader
	{
		private static readonly log4net.ILog log  = log4net.LogManager.GetLogger(typeof(QueryTranslator));
		private static StringCollection dontSpace = new StringCollection();

		private IDictionary typeMap = new SequencedHashMap();
		private IDictionary collections = new SequencedHashMap();
		private IList returnTypes = new ArrayList();
		private IList fromTypes = new ArrayList();
		private IList scalarTypes = new ArrayList();
		private IDictionary namedParameters = new Hashtable();
		private IDictionary aliasNames = new Hashtable();
		private ArrayList crossJoins = new ArrayList();

		// contains a List of strings
		private IList scalarSelectTokens = new ArrayList();

		// contains a List of strings containing Sql or SqlStrings
		private IList whereTokens = new ArrayList();
		private IList havingTokens = new ArrayList();
		private IDictionary joins = new Hashtable();
		private IList orderByTokens = new ArrayList();
		private IList groupByTokens = new ArrayList();
		private ArrayList identifierSpaces = new ArrayList();
		private ArrayList entitiesToFetch = new ArrayList();

		private IQueryable[] persisters;
		private string[] names;
		private bool[] includeInSelect;
		private IType[] types;
		private int selectLength;
		private string[][] scalarColumnNames;
		
		//--- PORT NOTE ---
		//original modifier was protected
		//I change in internal because Hql.SelectParser.AggregateType use factory.

		internal ISessionFactoryImplementor factory;

		//--- END NOTE ---
		private IDictionary replacements;
		private int count = 0;
		private int parameterCount = 0;
		private string queryString;
		private bool distinct = false;
		protected bool compiled;
		private string sqlString;
		private SqlCommand.SqlString realSqlString;
		private System.Type holderClass;
		private ConstructorInfo holderConstructor;
		private bool hasScalars;
		private bool shallowQuery;
		private QueryTranslator superQuery;
		
		private CollectionPersister collectionPersister;
		private int collectionOwnerColumn = -1;
		private string collectionOwnerName;
		private string fetchName;

		private string[] suffixes;

		/// <summary> 
		/// Construct a query translator
		/// </summary>
		public QueryTranslator(Dialect.Dialect d) : base(d)
		{
		}

		/// <summary>
		/// Compile a subquery
		/// </summary>
		/// <param name="superquery"></param>
		/// <param name="queryString"></param>
		internal protected void Compile(QueryTranslator superquery, string queryString) 
		{
			this.factory = superquery.factory;
			this.replacements = superquery.replacements;
			this.superQuery = superquery;
			this.shallowQuery = true;

			Compile(queryString);
		}

		/// <summary>
		/// Compile a "normal" query. This method may be called multiple
		/// times. Subsequent invocations are no-ops.
		/// </summary>
		/// <param name="factory"></param>
		/// <param name="queryString"></param>
		/// <param name="replacements"></param>
		/// <param name="scalar"></param>
		[MethodImpl(MethodImplOptions.Synchronized)]
		public void Compile(ISessionFactoryImplementor factory, string queryString, IDictionary replacements, bool scalar) 
		{
			if (!compiled) 
			{
				this.factory = factory;
				this.replacements = replacements;
				this.shallowQuery = scalar;

				Compile(queryString);
			}
		}

		/// <summary> 
		/// Compile the query (generate the SQL).
		/// </summary>
		protected void Compile(string queryString) 
		{
			this.queryString = queryString;

			log.Debug("compiling query");
			try 
			{
				ParserHelper.Parse(
					new PreprocessingParser(replacements),
					queryString,
					ParserHelper.HqlSeparators,
					this);
				RenderSql();
			} 
			catch (QueryException qe) 
			{
				qe.QueryString = queryString;
				throw qe;
			}
			catch (MappingException me) 
			{
				throw me;
			}
			catch (Exception e) 
			{
				log.Debug("unexpected query compilation problem", e);
				QueryException qe = new QueryException("Incorrect query syntax", e);
				qe.QueryString = queryString;
				throw qe;
			}
			
			PostInstantiate();

			compiled = true;
		}
		
		/// <summary>
		/// Persisters for the return values of a <c>Find</c> style query
		/// </summary>
		public override ILoadable[] Persisters 
		{
			get	
			{ 
				return persisters; 
			}			
		}
		
		/// <summary>
		///Types of the return values of an <tt>iterate()</tt> style query.
		///Return an array of <tt>Type</tt>s.
		/// </summary>
		public virtual IType[] ReturnTypes 
		{
			get	
			{ 
				return types;	
			}
		}

		protected bool HasScalarValues 
		{
			get 
			{ 
				return hasScalars; 
			}
		}
		
		public virtual string[][] ScalarColumnNames 
		{
			get	
			{ 
				return scalarColumnNames;
			}			
		}
		private void LogQuery(string hql, string sql) 
		{
			if (log.IsDebugEnabled) 
			{
				log.Debug("HQL: " + hql);
				log.Debug("SQL: " + sql);
			}
		}

		internal void SetAliasName(string alias, string name) 
		{
			aliasNames.Add(alias, name);
		}

		internal string GetAliasName(String alias) 
		{
			String name = (String) aliasNames[alias];
			if (name==null) 
			{
				if (superQuery!=null) 
				{
					name = superQuery.GetAliasName(alias);
				}
				else 
				{
					name = alias;
				}
			}
			return name;
		}

		internal string Unalias(string path) 
		{
			string alias = StringHelper.Root(path);
			string name = GetAliasName(alias);
			if (name!=null) 
			{
				return name + path.Substring( alias.Length );
			} 
			else 
			{
				return path;
			}
		}

		public void AddEntityToFetch(string name) 
		{
			entitiesToFetch.Add(name);
		}

		[Obsolete("Should use Hql.QueryTranslator.SqlString property instead")]
		public override string SQLString 
		{
			get 
			{ 
				LogQuery(queryString, sqlString); 
				return sqlString; 
			}
		}


		public override SqlString SqlString
		{
			get 
			{
				LogQuery( queryString, realSqlString.ToString() );
				return realSqlString;
			}
		}

		private int NextCount() 
		{
			return (superQuery==null) ? count++ : superQuery.count++;
		}

		internal string CreateName(string description) 
		{
			// this is a bit ugly, since Alias is really for
			// aliasing SQL identifiers ... but it does what
			// we want!
			return new SqlCommand.Alias(10, NextCount().ToString() + StringHelper.Underscore)
				.ToAliasString(StringHelper.Unqualify(description).ToLower(), dialect);
		}

		internal string CreateNameFor(System.Type type) 
		{
			return CreateName(type.Name);
		}

		internal string CreateNameForCollection(string role) 
		{
			return CreateName(role);
		}

		internal System.Type GetType(string name) 
		{
			System.Type type = (System.Type) typeMap[name];
			if ( type==null && superQuery!=null ) type = superQuery.GetType(name);
			return type;
		}

		internal string GetRole(string name) 
		{
			string role = (string) collections[name];
			if ( role==null && superQuery!=null ) role = superQuery.GetRole(name);
			return role;
		}

		internal bool IsName(string name) 
		{
			return aliasNames.Contains(name) ||
				typeMap.Contains(name) ||
				collections.Contains(name) || 
				( superQuery!=null && superQuery.IsName(name) );
		}

		internal IQueryable GetPersisterForName(string name) 
		{
			System.Type type = GetType(name);
			if (type == null) 
			{
				IType elemType = GetCollectionPersister( GetRole(name) ).ElementType;
				if ( ! (elemType is EntityType) ) return null;
				return GetPersister( ( (EntityType) elemType ).PersistentClass );
			} 
			else 
			{
				IQueryable persister = GetPersister(type);
				if (persister==null) throw new QueryException( "persistent class not found: " + type.Name );
				return persister;
			}
		}

		internal IQueryable GetPersisterUsingImports(string className) 
		{
			try 
			{
				return (IQueryable) factory.GetPersister( factory.GetImportedClassName( className) );
			}
			catch(Exception) 
			{
				return null;
			}
		}

		internal IQueryable GetPersister(System.Type clazz) 
		{
			try 
			{
				return (IQueryable) factory.GetPersister(clazz);
			} 
			catch (Exception) 
			{
				throw new QueryException( "persistent class not found: " + clazz.Name );
			}
		}

		internal CollectionPersister GetCollectionPersister(string role) 
		{
			try 
			{
				return factory.GetCollectionPersister(role);
			} 
			catch (Exception) 
			{
				throw new QueryException( "collection role not found: " + role );
			}
		}

		internal void AddType(string name, System.Type type) 
		{
			typeMap.Add(name, type);
		}

		internal void AddCollection(string name, string role) 
		{
			collections.Add(name, role);
		}

		internal void AddFrom(string name, System.Type type, SqlCommand.JoinFragment join) 
		{
			AddType(name, type);
			AddFrom(name, join);
		}

		internal void AddFrom(string name, SqlCommand.JoinFragment join) 
		{
			fromTypes.Add(name);
			AddJoin(name, join);
		}

		internal void AddFromClass(string name, ILoadable classPersister) 
		{
			SqlCommand.JoinFragment ojf = CreateJoinFragment(false);
			ojf.AddCrossJoin( classPersister.TableName, name );
			crossJoins.Add( name );
			AddFrom(name, classPersister.MappedClass, ojf);
		}

		internal void AddSelectClass(string name) 
		{
			returnTypes.Add(name);
		}

		internal void AddSelectScalar(IType type) 
		{
			scalarTypes.Add(type);
		}

		internal void AppendWhereToken(string token) 
		{
			whereTokens.Add(token);
		}

		internal void AppendWhereToken(SqlCommand.SqlString token) 
		{
			whereTokens.Add(token);
		}

		internal void AppendHavingToken(string token) 
		{
			havingTokens.Add(token);
		}

		internal void AppendOrderByToken(string token) 
		{
			orderByTokens.Add(token);
		}

		internal void AppendGroupByToken(string token) 
		{
			groupByTokens.Add(token);
		}

		internal void AppendScalarSelectToken(string token) 
		{
			scalarSelectTokens.Add(token);
		}

		internal void AppendScalarSelectTokens(string[] tokens) 
		{
			scalarSelectTokens.Add(tokens);
		}

		internal void AddJoin(string name, SqlCommand.JoinFragment newjoin) 
		{
			SqlCommand.JoinFragment oldjoin = (SqlCommand.JoinFragment) joins[name];
			if (oldjoin==null) 
			{
				joins.Add(name, newjoin);
			}
			else 
			{
				//TODO: HACKS with ToString() - fixed up
				oldjoin.AddCondition( newjoin.ToWhereFragmentString );
				//TODO: HACKS with ToString()
				if ( oldjoin.ToFromFragmentString.ToString().IndexOf( newjoin.ToFromFragmentString.ToString().Trim()) < 0 ) 
				{
					throw new AssertionFailure("bug in query parser: " + queryString);
					//TODO: what about the toFromFragmentString() ????
				}
			}
		}

		internal void AddNamedParameter(string name) 
		{
			if (superQuery != null) superQuery.AddNamedParameter(name);

			// want the param index to start at 0 instead of 1
			//int loc = ++parameterCount;
			int loc = parameterCount++;
			object o  = namedParameters[name];
			if (o == null) 
			{
				namedParameters.Add(name, loc);
			} 
			else if (o is int) 
			{
				ArrayList list = new ArrayList(4);
				list.Add(o);
				list.Add(loc);
				namedParameters[name] = list;
			} 
			else 
			{
				((ArrayList) o).Add(loc);
			}
		}

		internal int[] GetNamedParameterLocs(string name) 
		{
			object o = namedParameters[name];
			if (o == null) 
			{
				QueryException qe = new QueryException("Named parameter does not appear in Query: " + name);
				qe.QueryString = queryString;
				throw qe;
			} if (o is int) 
			  {
				  return new int[] { ((int) o) };
			  } 
			  else 
			  {
				  return ArrayHelper.ToIntArray( (ArrayList) o);
			  }
		}

		public ICollection NamedParameters 
		{
			get 
			{ 
				return namedParameters.Keys; 
			}
		}

		public static string ScalarName(int x, int y) 
		{
			return new StringBuilder()
				.Append('x')
				.Append(x)
				.Append(StringHelper.Underscore)
				.Append(y)
				.Append(StringHelper.Underscore)
				.ToString();
		}

		

		private void RenderSql() 
		{
			int rtsize;
			if (returnTypes.Count == 0 && scalarTypes.Count == 0) 
			{
				//ie no select clause in HQL
				returnTypes = fromTypes;
				rtsize = returnTypes.Count;
			}
			else
			{
				rtsize = returnTypes.Count;
				foreach(string entityName in entitiesToFetch)
					returnTypes.Add(entityName);
			}
			
			int size = returnTypes.Count;
			names = new string[size];
			persisters = new IQueryable[size];
			suffixes = new string[size];
			includeInSelect = new bool[size];
			for (int i=0; i<size; i++) 
			{
				string name = (string) returnTypes[i];
				//if ( !IsName(name) ) throw new QueryException("unknown type: " + name);
				persisters[i] = GetPersisterForName(name);
				suffixes[i] = (size==1) ? String.Empty : i.ToString() + StringHelper.Underscore;
				names[i] = name;
				includeInSelect[i] = !entitiesToFetch.Contains(name);
				if ( includeInSelect[i] ) selectLength++;
				if ( name.Equals(collectionOwnerName) ) collectionOwnerColumn = i;
			}

			string scalarSelect = RenderScalarSelect(); //Must be done here because of side-effect! yuck...

			int scalarSize = scalarTypes.Count;
			hasScalars = scalarTypes.Count!=size;

			types = new IType[scalarSize];
			for (int i=0; i<scalarSize; i++ ) 
			{
				types[i] = (IType) scalarTypes[i];
			}

			QuerySelect sql = new QuerySelect( factory.Dialect );
			sql.Distinct = distinct;

			if ( !shallowQuery ) 
			{
				RenderIdentifierSelect(sql);
				RenderPropertiesSelect(sql);
			}
			
			if ( CollectionPersister!=null ) 
			{
				sql.AddSelectFragmentString( collectionPersister.MultiselectClauseFragment(fetchName) );
			}
			if ( hasScalars || shallowQuery ) sql.AddSelectFragmentString(scalarSelect);

			// TODO: for some dialects it would be appropriate to add the renderOrderByProertySelecT() to other select strings
			MergeJoins( sql.JoinFragment );

			sql.SetWhereTokens(whereTokens);

			sql.SetGroupByTokens(groupByTokens);
			sql.SetHavingTokens(havingTokens);
			sql.SetOrderByTokens(orderByTokens);
			
			if ( CollectionPersister!=null && CollectionPersister.HasOrdering ) 
			{
				sql.AddOrderBy( CollectionPersister.GetSQLOrderByString(fetchName) );
			}

			scalarColumnNames = GenerateColumnNames(types, factory);

			// initialize the set of queries identifer spaces
			foreach(string name in collections.Values) 
			{
				CollectionPersister p = GetCollectionPersister(name);
				AddIdentifierSpace( p.QualifiedTableName );
			}
			foreach(string name in typeMap.Keys) 
			{
				IQueryable p = GetPersisterForName( name );
				AddIdentifierSpace( p.IdentifierSpace );
			}

//			sqlString = sql.ToQueryString();
			realSqlString = sql.ToQuerySqlString();


			System.Type[] classes = new System.Type[types.Length];
			for (int i=0; i<types.Length; i++) 
			{
				if (types[i]!=null) classes[i] = (types[i] is PrimitiveType) ? 
										((PrimitiveType) types[i]).PrimitiveClass :
										types[i].ReturnedClass;
			}

			try 
			{
				if (holderClass!=null) holderConstructor = holderClass.GetConstructor(classes);
			} 
			catch(Exception nsme) 
			{
				throw new QueryException("could not find constructor for: " + holderClass.Name, nsme);
			}
		}

		private void RenderIdentifierSelect(QuerySelect sql) 
		{
			int size = returnTypes.Count;

			for (int k=0; k<size; k++) 
			{
				string name = (string) returnTypes[k];
				string suffix = size==1 ? String.Empty : k.ToString() + StringHelper.Underscore;
				//TODO: HACK with ToString()
				sql.AddSelectFragmentString( persisters[k].IdentifierSelectFragment(name, suffix).ToString() );
			}
		}

		private string RenderOrderByPropertiesSelect() 
		{
			StringBuilder buf = new StringBuilder(10);
		
			//add the columns we are ordering by to the select ID select clause
			foreach(string token in orderByTokens)
			{
				if ( token.LastIndexOf(".") > 0 ) 
				{
					//ie. it is of form "foo.bar", not of form "asc" or "desc"
					buf.Append(StringHelper.CommaSpace).Append(token);
				}
			}
		
			return buf.ToString();
		}

		private void RenderPropertiesSelect(QuerySelect sql) 
		{
			int size = returnTypes.Count;
			for (int k=0; k<size; k++) 
			{
				string suffix = (size==1) ? String.Empty : k.ToString() + StringHelper.Underscore;
				string name = (string) returnTypes[k];
				//TODO: HACK with ToString()
				sql.AddSelectFragmentString( persisters[k].PropertySelectFragment(name, suffix).ToString() );
			}
		}

		/// <summary> 
		/// WARNING: side-effecty
		/// </summary>
		private string RenderScalarSelect() 
		{
			bool isSubselect = superQuery != null;
			
			StringBuilder buf = new StringBuilder(20);
			
			if (scalarTypes.Count == 0) 
			{
				//ie. no select clause
				int size = returnTypes.Count;
				for (int k=0; k<size; k++) 
				{
					scalarTypes.Add(NHibernate.Entity(persisters[k].MappedClass));
					
					string[] names = persisters[k].IdentifierColumnNames;
					for (int i=0; i<names.Length; i++) 
					{
						buf.Append(returnTypes[k]).Append(StringHelper.Dot).Append(names[i]);
						if (!isSubselect) buf.Append(" as ").Append(ScalarName(k, i)); 
						if (i != names.Length - 1 || k != size - 1) buf.Append(StringHelper.CommaSpace);
					}
				}
			} 
			else 
			{
				//there _was_ a select clause
				int c = 0;
				bool nolast = false; //real hacky...
				foreach (object next in scalarSelectTokens) 
				{
					if (next is string) 
					{
						string token = (string) next;
						string lc = token.ToLower();
						if (lc.Equals(StringHelper.CommaSpace)) 
						{
							if (nolast) 
							{
								nolast = false;
							}
							else 
							{
								if (!isSubselect) buf.Append(" as ").Append(ScalarName(c++, 0));
							}
						}
						buf.Append(token);
						if (lc.Equals("distinct") || lc.Equals("all")) 
						{
							buf.Append(' ');
						}
					} 
					else 
					{
						nolast = true;
						string[] tokens = (string[]) next;
						for (int i = 0; i < tokens.Length; i++) 
						{
							buf.Append(tokens[i]);
							if (!isSubselect) buf.Append(" as ").Append(ScalarName(c, i));
							if (i != tokens.Length - 1) buf.Append(StringHelper.CommaSpace);
						}
						c++;
					}
				}
				if (!isSubselect && !nolast) buf.Append(" as ").Append(ScalarName(c++, 0));
				
			}
			
			return buf.ToString();
		}

		private SqlCommand.JoinFragment MergeJoins(SqlCommand.JoinFragment ojf) 
		{
			//classes
			foreach(string name in typeMap.Keys) 
			{
				IQueryable p = GetPersisterForName(name);
				bool includeSubclasses = returnTypes.Contains(name) && !IsShallowQuery;

				SqlCommand.JoinFragment join = (SqlCommand.JoinFragment) joins[name];
				if (join!=null) 
				{
					bool isCrossJoin = crossJoins.Contains(name);
					ojf.AddFragment(join);
					
					ojf.AddJoins(
						p.FromJoinFragment(name, isCrossJoin, includeSubclasses),
						p.QueryWhereFragment(name, isCrossJoin, includeSubclasses)
						);
				}
			}

			foreach(string name in collections.Keys) 
			{
				SqlCommand.JoinFragment collJoin = (SqlCommand.JoinFragment) joins[name];
				if (collJoin!=null) ojf.AddFragment(collJoin);
			}

			return ojf;
		}

		public IList QuerySpaces 
		{
			get 
			{ 
				return identifierSpaces; 
			}
		}

		/// <summary>
		/// Is this query called by Scroll() or Iterate()?
		/// </summary>
		/// <value>true if it is, false if it is called by find() or list()</value>
		public bool IsShallowQuery 
		{
			get 
			{ 
				return shallowQuery; 
			}
		}

		internal void AddIdentifierSpace(object table) 
		{
			identifierSpaces.Add(table);
			if (superQuery!=null) superQuery.AddIdentifierSpace(table);
		}

		internal bool Distinct 
		{
			set 
			{ 
				distinct = value; 
			}
		}
		
		public bool IsSubquery
		{
			get
			{
				return superQuery!=null;
			}
		}

		protected override CollectionPersister CollectionPersister 
		{
			get 
			{ 
				return collectionPersister; 
			}
		}

		public void SetCollectionToFetch(string role, string name, string ownerName)
		{
			fetchName = name;
			collectionPersister = GetCollectionPersister(role);
			collectionOwnerName = ownerName;
		}

		protected override string[] Suffixes 
		{
			get 
			{ 
				return suffixes; 
			}
		}

		protected void AddFromCollection(string elementName, string collectionRole) 
		{
			//q.addCollection(collectionName, collectionRole);
			IType collectionElementType = GetCollectionPersister(collectionRole).ElementType;
			if ( !collectionElementType.IsEntityType ) throw new QueryException(
														   "collection of values in filter: " + elementName);
			EntityType elemType = (EntityType) collectionElementType;

			CollectionPersister persister = GetCollectionPersister(collectionRole);
			string[] keyColumnNames = persister.KeyColumnNames;
			IType keyType = persister.KeyType;
			//if (keyColumnNames.Length!=1) throw new QueryException("composite-key collecion in filter: " + collectionRole);

			string collectionName;
			SqlCommand.JoinFragment join = CreateJoinFragment(false);
			collectionName = persister.IsOneToMany ? elementName : CreateNameForCollection(collectionRole);
			join.AddCrossJoin( persister.QualifiedTableName, collectionName);
			if ( !persister.IsOneToMany ) 
			{
				//many-to-many
				AddCollection(collectionName, collectionRole);

				IQueryable p = GetPersister( elemType.PersistentClass );
				string[] idColumnNames = p.IdentifierColumnNames;
				string[] eltColumnNames = persister.ElementColumnNames;
				join.AddJoin(
					p.TableName,
					elementName,
					StringHelper.Prefix(eltColumnNames, collectionName + StringHelper.Dot),
					idColumnNames,
					SqlCommand.JoinType.InnerJoin);
			}
			join.AddCondition( collectionName, keyColumnNames, " = ", keyType, Factory);
			if (persister.HasWhere) join.AddCondition(persister.GetSQLWhereString(collectionName));
			AddFrom(elementName, elemType.PersistentClass, join);
		}

		private IDictionary pathAliases = new Hashtable();
		private IDictionary pathJoins = new Hashtable();

		internal string GetPathAlias(string path) 
		{
			return (string) pathAliases[path];
		}

		internal SqlCommand.JoinFragment GetPathJoin(string path) 
		{
			return (SqlCommand.JoinFragment) pathJoins[path];
		}

		internal void AddPathAliasAndJoin(string path, string alias, SqlCommand.JoinFragment join) 
		{
			pathAliases.Add(path, alias);
			pathJoins.Add( path, join.Copy() );
		}

		protected override int BindNamedParameters(IDbCommand ps, IDictionary namedParams, int start, ISessionImplementor session) 
		{
			if (namedParams != null) 
			{
				// assumes that types are all of span 1
				int result = 0;
				foreach (DictionaryEntry e in namedParams) 
				{
					string name = (string) e.Key;
					TypedValue typedval = (TypedValue) e.Value;
					int[] locs = GetNamedParameterLocs(name);
					for (int i = 0; i < locs.Length; i++) 
					{
						// Hack: parametercollection starts at 0
						//typedval.Type.NullSafeSet(ps, typedval.Value, Impl.AdoHack.ParameterPos(locs[i] + start), session);
						typedval.Type.NullSafeSet(ps, typedval.Value, (locs[i] + start), session);
						// end-of Hack
					}
					result += locs.Length;
				}
				return result;
			}
			else 
			{
				return 0;
			}
		}

		//TODO: H2.0.3: iterate and scroll instead of getenumerable
		public IEnumerable GetEnumerable(object[] values, IType[] types, RowSelection selection, 
			IDictionary namedParams, IDictionary lockModes, ISessionImplementor session) 
		{
			//TODO: this is a major hack - apply locks is not working with a string of Sql so we need
			// to give it a SqlString...
			//http://jira.nhibernate.org:8080/browse/NH-64
			// ApplyLocks(SqlString, lockModes, session.Factory.Dialect).ToString(),
			// this works because it is just appending strings and not doing any 
//			string sqlWithLock = ApplyLocks(new SqlString(SQLString), lockModes, session.Factory.Dialect).ToString();
			SqlString sqlWithLock = ApplyLocks(SqlString, lockModes, session.Factory.Dialect);


			IDbCommand st = PrepareQueryStatement(
				sqlWithLock,
				values, types, namedParams, selection, false, session);
			try 
			{
				SetMaxRows(st, selection);

				//TODO: H2.0.3 - uses session.Batcher.GetResultSet(st) instead
				// of directly executing the reader - the Batcher can be smarter 
				// about when to wrap the IDataReader in an NDataReader
				IDataReader rs = st.ExecuteReader();
				
				//TODO: make this a much smarter implementation that looks at the Type
				// that is being loaded and determines wether or not to wrap this IDataReader
				// in a NDataReader.  I believe the problem of multiple readers being opened
				// are occuring in <composite-id> with <many-to-one> and <component> with
				// a <collection>, <many-to-one>, and <one-to-one> inside of them.  I believe
				// this is caused when the NullSafeGet() method calls other methods that can 
				// potentially perform a Load of another object.
				if(!session.Factory.ConnectionProvider.Driver.SupportsMultipleOpenReaders) 
				{
					rs = new Driver.NDataReader(rs); 
				}

				Advance(rs, selection, session);
				return new EnumerableImpl(rs, session, ReturnTypes, ScalarColumnNames );
			} 
			catch (Exception e) 
			{
				ClosePreparedStatement(st, selection, session);
				throw e;
			}
		}

		public static string[] ConcreteQueries(string query, ISessionFactoryImplementor factory) 
		{
			//scan the query string for class names appearing in the from clause and replace 
			//with all persistent implementors of the class/interface, returning multiple 
			//query strings (make sure we don't pick up a class in the select clause!) 

			//TODO: this is one of the ugliest and most fragile pieces of code in Hibernate...
			string[] tokens = StringHelper.Split( ParserHelper.Whitespace + "(),", query, true ); 
			if (tokens.Length==0) return new String[] { query }; // just especially for the trivial collection filter 

			ArrayList placeholders = new ArrayList();
			ArrayList replacements = new ArrayList();
			StringBuilder templateQuery = new StringBuilder(40);
			int count = 0;
			string last = null;
			int nextIndex = 0;
			string next = null;
			templateQuery.Append( tokens[0] );
			for (int i=1; i<tokens.Length; i++) 
			{
				//update last non-whitespace token, if necessary
				if ( !ParserHelper.IsWhitespace( tokens[i-1] ) ) last = tokens[i-1].ToLower();

				string token = tokens[i];
				if ( !ParserHelper.IsWhitespace(token) || last==null ) 
				{
					// scan for the next non-whitespace token
					if (nextIndex<=i) 
					{
						for ( nextIndex=i+1; nextIndex<tokens.Length; nextIndex++ ) 
						{
							next = tokens[nextIndex].ToLower();
							if ( !ParserHelper.IsWhitespace(next) ) break;
						}
					}

					//if ( Character.isUpperCase( token.charAt( token.lastIndexOf(".") + 1 ) ) ) {
					if (
						( beforeClassTokens.Contains(last) && !notAfterClassTokens.Contains(next) ) ||
						"class".Equals(last) ) 
					{
						System.Type clazz = GetImportedClass(token, factory);
						if ( clazz!=null ) 
						{
							string[] implementors = factory.GetImplementors(clazz);
							string placeholder = "$clazz" + count++ + "$";
						
							if ( implementors!=null ) 
							{
								placeholders.Add(placeholder);
								replacements.Add(implementors);
							}
							token = placeholder; //Note this!!
						}
					}
				}
				templateQuery.Append( token );

			}
			string[] results = StringHelper.Multiply( templateQuery.ToString(), placeholders.GetEnumerator(), replacements.GetEnumerator() );
			if(results.Length == 0)
				log.Warn("no persistent classes found for query class: "+query);
			return results;
		}


		private static readonly IList beforeClassTokens = new ArrayList();
		private static readonly IList notAfterClassTokens = new ArrayList();

		static QueryTranslator() 
		{
			beforeClassTokens.Add("from");
			//beforeClassTokens.Add("new"); DEFINITELY DON'T HAVE THIS!!
			beforeClassTokens.Add(",");
			notAfterClassTokens.Add("in");
			//notAfterClassTokens.Add(",");
			notAfterClassTokens.Add("from");
			notAfterClassTokens.Add(")");
		}

		internal System.Type GetImportedClass(string name) 
		{
			return GetImportedClass(name, factory);
		}

		private static System.Type GetImportedClass(string name, ISessionFactoryImplementor factory) 
		{
			try 
			{
				return ReflectHelper.ClassForName( factory.GetImportedClassName(name) );
			} 
			catch(Exception) 
			{
				return null;
			}
		}

		private static string[][] GenerateColumnNames(IType[] types, ISessionFactoryImplementor f) 
		{
			string[][] names = new string[types.Length][];
			for (int i=0; i<types.Length; i++) 
			{
				int span = types[i].GetColumnSpan(f);
				names[i] = new string[span];
				for (int j=0; j<span; j++) 
				{
					names[i][j] = ScalarName(i, j);
				}
			}
			return names;
		}

		public IList FindList(
			ISessionImplementor session,
			object[] values,
			IType[] types,
			bool returnProxies,
			RowSelection selection,
			IDictionary namedParams,
			IDictionary lockModes) 
		{

			// TODO: fix the last parameter that is suppoesd to use lockModes
			return base.Find(session, values, types, returnProxies, selection, namedParams, lockModes);
		}

		protected override object GetResultColumnOrRow(object[] row, IDataReader rs, ISessionImplementor session) 
		{
			IType[] returnTypes = ReturnTypes;
			row = ToResultRow(row);
			if (hasScalars) 
			{
				string[][] names = ScalarColumnNames;
				int queryCols = returnTypes.Length;
				if ( holderClass==null && queryCols==1 ) 
				{
					return returnTypes[0].NullSafeGet( rs, names[0], session, null );
				} 
				else 
				{
					row = new object[queryCols];
					for (int i=0; i<queryCols; i++ ) 
					{
						row[i] = returnTypes[i].NullSafeGet( rs, names[i], session, null );
					}
					if ( holderClass==null ) return row;
				}
			} 
			else if (holderClass==null) 
			{
				return (row.Length==1) ? row[0] : row;
			}

			try 
			{
				return holderConstructor.Invoke(row);
			} 
			catch (Exception e) 
			{
				throw new QueryException("could not instantiate: " + holderClass, e);
			}
		}

		private object[] ToResultRow(object[] row) 
		{
			if (selectLength == row.Length) 
			{
				return row;
			}
			else 
			{
				object[] result = new object[selectLength];
				int j=0;
				for (int i=0; i<row.Length; i++) 
				{
					if ( includeInSelect[i] ) result[j++] = row[i];
				}
				return result;
			}
		}

		internal SqlCommand.QueryJoinFragment CreateJoinFragment(bool useThetaStyleInnerJoins) 
		{
			return new SqlCommand.QueryJoinFragment( factory.Dialect, useThetaStyleInnerJoins );
		}

		internal System.Type HolderClass 
		{
			set { holderClass = value; }
		}

		protected override LockMode[] GetLockModes(IDictionary lockModes) 
		{
			IDictionary nameLockModes = new Hashtable();
			if (lockModes!=null) 
			{
				IDictionaryEnumerator it = lockModes.GetEnumerator();
				while ( it.MoveNext() ) 
				{
					DictionaryEntry me = it.Entry;
					nameLockModes.Add( 
						GetAliasName( (String) me.Key ),
						me.Value
						);
				}
			}
			LockMode[] lockModeArray = new LockMode[names.Length];
			for ( int i=0; i < names.Length; i++ ) 
			{
				LockMode lm = (LockMode) nameLockModes[names[i]];
				if (lm==null) lm = LockMode.None;
				lockModeArray[i] = lm;
			}
			return lockModeArray;
		}

		protected override SqlString ApplyLocks(SqlString sql, IDictionary lockModes, Dialect.Dialect dialect)
		{
			if (lockModes == null || lockModes.Count == 0)
			{
				return sql;
			}
			else if (dialect.SupportsForUpdateOf)
			{
				LockMode upgradeType = null;
				ForUpdateFragment updateClause = new ForUpdateFragment();
				IDictionaryEnumerator it = lockModes.GetEnumerator();
				it.MoveNext();
				DictionaryEntry me = (DictionaryEntry) it.Current;
				String name = GetAliasName( (String) me.Key );
				LockMode lockMode = (LockMode) me.Value;
				if ( LockMode.Read.LessThan(lockMode) ) 
				{
					updateClause.AddTableAlias(name);
					if ( upgradeType!=null && lockMode!=upgradeType ) throw new QueryException("mixed LockModes");
					upgradeType = lockMode;
				}
				if ( upgradeType==LockMode.UpgradeNoWait && dialect.SupportsForUpdateNoWait) 
				{ 
					updateClause.NoWait = true;
				}

				return sql.Append(updateClause.ToSqlStringFragment());
			}
			else
			{
				log.Debug("dialect does not support FOR UPDATE OF");
				return sql;
			}
		}

		protected override bool UpgradeLocks() 
		{
			return true;
		}

		protected override int CollectionOwner
		{
			get
			{
				return collectionOwnerColumn;
			}
		}

		protected ISessionFactoryImplementor Factory
		{
			set
			{
				this.factory = value;
			}
			get
			{
				return factory;
			}
		}

		protected bool Compiled
		{
			get
			{
				return compiled;
			}
		}

		public IDictionary AggregateFunctions
		{
			get
			{
				return factory.Dialect.AggregateFunctions;
			}
		}

		/// <summary>
		/// Obtain an <see cref="IDbCommand"/> and bind the parameters.
		/// </summary>
		/// <param name="sql">A string containing the Sql statement</param>
		/// <param name="values">The values to put in the Parameters.</param>
		/// <param name="types">The IType to use to put the values in the Parameters.</param>
		/// <param name="selection"></param>
		/// <param name="scroll"></param>
		/// <param name="session">The ISession currently in.</param>
		/// <returns>An IDbCommand that is ready for execution.</returns>
		/// <remarks>
		/// <para>
		/// This is used to convert an Hql sql string into a SqlString and then finally into an IDbCommand
		/// that has been prepared.
		/// </para>
		/// <para>
		/// This should not be considered a permanent solution.  While parsing the HQL we should be building
		/// a SqlString, not a string that contains sql...
		/// </para>
		/// <para>
		/// This used to be in the Loader, but since the only use of this right now is in the QueryTranslator
		/// I moved it into here.
		/// </para>
		/// </remarks>
		protected override IDbCommand PrepareQueryStatement(string sql, object[] values, IType[] types, IDictionary namedParams, RowSelection selection, bool scroll, ISessionImplementor session) 
		{
			return PrepareQueryStatement( new SqlString(sql), values, types, namedParams, selection, scroll, session);
		}

		protected override IDbCommand PrepareQueryStatement(SqlString sql, object[] values, IType[] types, IDictionary namedParams, RowSelection selection, bool scroll, ISessionImplementor session) 
		{
            IType[] paramTypes = null;
			object[] paramValues = null;

			if(namedParams==null || namedParams.Count==0) 
			{
				paramTypes = types;
				paramValues = values;
			}
			else 
			{
				// convert the named parameters to an array of values and types
				ArrayList paramTypeList = new ArrayList(namedParams.Keys.Count);
				ArrayList paramValueList = new ArrayList(namedParams.Keys.Count);
				
				// assumes that types are all of span 1
				foreach (DictionaryEntry e in namedParams) 
				{
					string name = (string) e.Key;
					TypedValue typedval = (TypedValue) e.Value;
					int[] locs = GetNamedParameterLocs(name);

					for (int i = 0; i < locs.Length; i++) 
					{
						int lastInsertedIndex = paramTypeList.Count;

						int insertAt = locs[i];
						
						// need to make sure that the ArrayList is populated with null objects
						// up to the index we are about to add the values into.  An Index Out 
						// of Range exception will be thrown if we add an element at an index
						// that is greater than the Count.
						if(insertAt >= lastInsertedIndex)
						{
							for(int j = lastInsertedIndex; j <= insertAt; j++) 
							{
								paramTypeList.Add(null);
								paramValueList.Add(null);
							}
						}
						
						paramTypeList[insertAt] = typedval.Type; 
						paramValueList[insertAt] = typedval.Value;
					}
					
				}
				paramTypes = (IType[]) paramTypeList.ToArray( typeof(IType) );
				paramValues = (object[]) paramValueList.ToArray( typeof(object) );
			}

			
//			StringTokenizer tokenizer = new StringTokenizer(sql, StringHelper.SqlParameter, true);
//			string[] tokens =  sql.Split(StringHelper.SqlParameter[0]);
//
//			SqlStringBuilder hqlToSqlBuilder = new SqlStringBuilder(types.Length * 2);
			SqlStringBuilder hqlBuilder = new SqlStringBuilder(sql); //this.SqlString			
//			IEnumerator tokenEnum = tokenizer.GetEnumerator();
//			string previousToken = String.Empty;
//			string token = String.Empty;
			int paramIndex = 0;
//
//			while(tokenEnum.MoveNext()) 
			for( int i=0; i<hqlBuilder.Count; i++ ) 
			{
//				token = (string)tokenEnum.Current;
				Parameter partParam = hqlBuilder[i] as Parameter;
				if(partParam!=null) 
				{
					Parameter param = Parameter.GenerateParameters(session.Factory, new string[] {paramIndex.ToString()}, paramTypes[paramIndex])[0];
					hqlBuilder[i] = param;
					paramIndex++;
				}
//				if(token.Equals(StringHelper.SqlParameter) 
//					&& ( EndsWithBoolOperator( previousToken ) ) ) //.EndsWith("=") || previousToken.EndsWith("= ") ) ) 
//				{
//					Parameter param = Parameter.GenerateParameters(session.Factory, new string[]{paramIndex.ToString()}, paramTypes[paramIndex])[0];
//					hqlToSqlBuilder.Add(param);
//					paramIndex++;
//				}
//				else 
//				{
//					hqlToSqlBuilder.Add(token);
//				}
//
//				previousToken = token;
			}
			
//			return PrepareCommand(hqlToSqlBuilder.ToSqlString(), paramValues, paramTypes, null, selection, scroll, session);
			return PrepareCommand(hqlBuilder.ToSqlString(), paramValues, paramTypes, null, selection, scroll, session);
		}

		/// <summary>
		/// Temp method to help us figure out if the string ends with character that indicate that
		/// a parameter is likely to follow.
		/// </summary>
		/// <param name="sqlFragment">A string that contains sql.</param>
		/// <returns>true when a Parameter might follow this sql.</returns>
		private bool EndsWithBoolOperator(string sqlFragment) 
		{
			Regex r = new Regex(WhereParser.BooleanOperatorsAsRegEx, RegexOptions.IgnoreCase | RegexOptions.Compiled);
			return r.Match(sqlFragment).Success;
		}
	}
}