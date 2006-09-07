using System;
using System.Data;
using System.Text;
using NHibernate.SqlCommand;
using NHibernate.Util;
using Environment = NHibernate.Cfg.Environment;
using NHibernate.SqlTypes;

namespace NHibernate.Driver
{
	/// <summary>
	/// Base class for the implementation of IDriver
	/// </summary>
	public abstract class DriverBase : IDriver
	{
		private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(DriverBase));

		public abstract IDbConnection CreateConnection();
		public abstract IDbCommand CreateCommand();

		public abstract bool UseNamedPrefixInSql { get; }
		public abstract bool UseNamedPrefixInParameter { get; }
		public abstract string NamedPrefix { get; }

		public string FormatNameForSql(string parameterName)
		{
			return UseNamedPrefixInSql ? (NamedPrefix + parameterName) : StringHelper.SqlParameter;
		}

		public string FormatNameForParameter(string parameterName)
		{
			return UseNamedPrefixInParameter ? (NamedPrefix + parameterName) : parameterName;
		}

		public virtual bool SupportsMultipleOpenReaders
		{
			get { return true; }
		}

		public virtual bool SupportsPreparingCommands
		{
			get { return true; }
		}

		public virtual IDbCommand GenerateCommand(CommandType type, SqlString sqlString)
		{
			int paramIndex = 0;
			IDbCommand cmd = CreateCommand();
			cmd.CommandType = type;

			object envTimeout = Environment.Properties[Environment.CommandTimeout];
			if (envTimeout != null)
			{
				int timeout = Convert.ToInt32(envTimeout);
				if (timeout >= 0)
				{
					if (log.IsDebugEnabled)
					{
						log.Debug(string.Format("setting ADO Command timeout to '{0}' seconds", timeout));
					}
					try
					{
						cmd.CommandTimeout = timeout;
					}
					catch (Exception e)
					{
						if (log.IsWarnEnabled)
						{
							log.Warn(e.ToString());
						}
					}
				}
				else
				{
					log.Error("Invalid timeout of '" + envTimeout + "' specified, ignoring");
				}
			}

			StringBuilder builder = new StringBuilder(sqlString.SqlParts.Count * 15);
			SqlType[] parameterTypes = sqlString.ParameterTypes;
			foreach (object part in sqlString.SqlParts)
			{
				Parameter parameter = part as Parameter;

				if (parameter != null)
				{
					string paramName = "p" + paramIndex;
					builder.Append(FormatNameForSql(paramName));

					IDbDataParameter dbParam = GenerateParameter(cmd, paramName, parameterTypes[paramIndex]);

					cmd.Parameters.Add(dbParam);

					paramIndex++;
				}
				else
				{
					builder.Append((string) part);
				}
			}

			cmd.CommandText = builder.ToString();

			return cmd;
		}

		protected virtual void InitializeParameter(IDbDataParameter dbParam, string name, SqlType sqlType)
		{
			dbParam.ParameterName = FormatNameForParameter(name);
			dbParam.DbType = sqlType.DbType;
		}

		/// <summary>
		/// Generates an IDbDataParameter for the IDbCommand.  It does not add the IDbDataParameter to the IDbCommand's
		/// Parameter collection.
		/// </summary>
		/// <param name="command">The IDbCommand to use to create the IDbDataParameter.</param>
		/// <param name="name">The name to set for IDbDataParameter.Name</param>
		/// <param name="sqlType">The SqlType to set for IDbDataParameter.</param>
		/// <returns>An IDbDataParameter ready to be added to an IDbCommand.</returns>
		protected IDbDataParameter GenerateParameter(IDbCommand command, string name, SqlType sqlType)
		{
			if (name != null && sqlType == null)
			{
				throw new QueryException(String.Format("No type assigned to parameter '{0}': be sure to set types for named parameters.", name));
			}

			IDbDataParameter dbParam = command.CreateParameter();
			InitializeParameter(dbParam, name, sqlType);

			return dbParam;
		}

		/// <summary>
		/// Prepare the <paramref name="command" />. Will only be called if <see cref="SupportsPreparingCommands" />
		/// is <c>true</c>.
		/// </summary>
		/// <remarks>
		/// Drivers that require Size or Precision/Scale to be set before the IDbCommand is prepared should 
		/// override this method and use the info contained in <paramref name="parameterTypes" /> to set those
		/// values.  By default those values are not set, only the DbType and ParameterName are set.
		/// </remarks>
		public virtual void PrepareCommand(IDbCommand command, SqlType[] parameterTypes)
		{
			command.Prepare();
		}
	}
}