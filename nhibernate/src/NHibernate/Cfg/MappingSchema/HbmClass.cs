namespace NHibernate.Cfg.MappingSchema
{
	partial class HbmClass : AbstractDecoratable, IEntityMapping, IEntityDiscriminableMapping
	{
		public HbmId Id
		{
			get { return Item as HbmId; }
		}

		public HbmCompositeId CompositeId
		{
			get { return Item as HbmCompositeId; }
		}

		public HbmVersion Version
		{
			get { return Item1 as HbmVersion; }
		}

		public HbmTimestamp Timestamp
		{
			get { return Item1 as HbmTimestamp; }
		}

		#region Implementation of IEntityMapping

		protected override HbmMeta[] Metadatas
		{
			get { return meta ?? new HbmMeta[0]; }
		}

		public string EntityName
		{
			get { return entityname; }
		}

		public string Name
		{
			get { return name; }
		}

		public string Node
		{
			get { return node; }
		}

		public string Proxy
		{
			get { return proxy; }
		}

		public bool? UseLazy
		{
			get { return lazySpecified ? lazy : (bool?)null; }
		}

		public HbmTuplizer[] Tuplizers
		{
			get { return tuplizer ?? new HbmTuplizer[0]; }
		}

		public bool DynamicUpdate
		{
			get { return dynamicupdate; }
		}

		public bool DynamicInsert
		{
			get { return dynamicinsert; }
		}

		public int? BatchSize
		{
			get { return batchsizeSpecified ? batchsize : (int?) null; }
		}

		public bool SelectBeforeUpdate
		{
			get { return selectbeforeupdate; }
		}

		public string Persister
		{
			get { return persister; }
		}

		public bool? IsAbstract
		{
			get { return abstractSpecified ? @abstract : (bool?) null; }
		}

		public HbmSynchronize[] Synchronize
		{
			get { return synchronize ?? new HbmSynchronize[0]; }
		}

		#endregion

		#region Implementation of IEntityDiscriminableMapping

		public string DiscriminatorValue
		{
			get { return discriminatorvalue; }
		}

		#endregion

		#region Implementation of IEntitySqlsMapping

		public HbmLoader SqlLoader
		{
			get { return loader; }
		}

		public HbmCustomSQL SqlInsert
		{
			get { return sqlinsert; }
		}

		public HbmCustomSQL SqlUpdate
		{
			get { return sqlupdate; }
		}

		public HbmCustomSQL SqlDelete
		{
			get { return sqldelete; }
		}

		#endregion
	}
}