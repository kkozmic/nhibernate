using System;
using NHibernate.Loader;
using NHibernate.SqlCommand;
using NHibernate.Type;

namespace NHibernate.Persister
{
	/// <summary>
	/// A <c>ClassPersister</c> that may be loaded by outer join using
	/// the <c>OuterJoinLoader</c> hierarchy and may be an element
	/// of a one-to-many association.
	/// </summary>
	public interface IOuterJoinLoadable
	{
		/// <summary>
		/// Get the names of columns used to persist the identifier
		/// </summary>
		string[] IdentifierColumnNames { get; }

		/// <summary>
		/// Get the name of the column used as a discriminator
		/// </summary>
		string DiscriminatorColumnName { get; }

		//USED BY OuterJoinLoader + subclasses

		/// <summary>
		/// How many properties are there, for this class and all subclasses? (optional operation)
		/// </summary>
		/// <returns></returns>
		int CountSubclassProperties();

		/// <summary>
		/// May this property be fetched using an SQL outerjoin?
		/// </summary>
		/// <param name="i"></param>
		/// <returns></returns>
		OuterJoinFetchStrategy EnableJoinedFetch( int i );

		/// <summary>
		/// Is this property defined on a subclass of the mapped class?
		/// </summary>
		/// <param name="i"></param>
		/// <returns></returns>
		bool IsDefinedOnSubclass( int i );

		/// <summary>
		/// Get an array of the types of all properties of all subclasses (optional operation)
		/// </summary>
		/// <param name="i"></param>
		/// <returns></returns>
		IType GetSubclassPropertyType( int i );

		/// <summary>
		/// Get the name of the numbered property of the class or a subclass
		/// (optional operation)
		/// </summary>
		/// <param name="i"></param>
		/// <returns></returns>
		string GetSubclassPropertyName( int i );

		/// <summary>
		/// Return the column names used to persist all properties of all sublasses of the persistent class
		/// (optional operation)
		/// </summary>
		string[ ] GetSubclassPropertyColumnNames( int i );

		/// <summary>
		/// Return the table name used to persist the numbered property of 
		/// the class or a subclass
		/// (optional operation)
		/// </summary>
		string GetSubclassPropertyTableName( int i );

		/// <summary>
		/// Given the number of a property of a subclass, and a table alias, return the aliased column names
		/// (optional operation)
		/// </summary>
		/// <param name="name"></param>
		/// <param name="i"></param>
		/// <returns></returns>
		string[ ] ToColumns( string name, int i );

		/// <summary>
		/// Given a query alias and an identifying suffix, render the identifier select fragment
		/// </summary>
		/// <param name="name"></param>
		/// <param name="suffix"></param>
		/// <returns></returns>
		SqlString IdentifierSelectFragment( string name, string suffix );

		/// <summary>
		/// Given a query alias and an identifying suffix, render the property select fragment
		/// (optional operation)
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="suffix"></param>
		/// <returns></returns>
		SqlString PropertySelectFragment( string alias, string suffix );

		/// <summary>
		/// Get the main from table fragment, given a query alias (optional operation)
		/// </summary>
		/// <param name="alias"></param>
		/// <returns></returns>
		SqlString FromTableFragment( string alias );

		/// <summary>
		/// Generate a list of collection index and element columns
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="suffix"></param>
		/// <returns></returns>
		SqlString SelectFragment( string alias, string suffix );

		/// <summary>
		/// Get the where clause part of any joins (optional operation)
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="innerJoin"></param>
		/// <param name="includeSubclasses"></param>
		/// <returns></returns>
		SqlString WhereJoinFragment( string alias, bool innerJoin, bool includeSubclasses );

		/// <summary>
		/// Get the from clause part of any joins (optional operation)
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="innerJoin"></param>
		/// <param name="includeSubclasses"></param>
		/// <returns></returns>
		SqlString FromJoinFragment( string alias, bool innerJoin, bool includeSubclasses );
	}
}
