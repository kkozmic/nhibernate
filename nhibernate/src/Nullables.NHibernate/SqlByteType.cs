using System;
using System.Data;
using System.Data.SqlTypes;
using NHibernate.SqlTypes;

namespace Nullables.NHibernate
{
	public class SqlByteType : SqlTypesType
	{
		public SqlByteType() : base( new ByteSqlType() )
		{
		}

		public override object Get( IDataReader rs, int index )
		{
			return new SqlByte( Convert.ToByte( rs[ index ] ) );
		}

		protected override object GetValue( INullable value )
		{
			return ( ( SqlByte ) value ).Value;
		}

		public override object FromStringValue( string xml )
		{
			return SqlByte.Parse( xml );
		}

		public override Type ReturnedClass
		{
			get { return typeof( SqlByte ); }
		}
	}
}