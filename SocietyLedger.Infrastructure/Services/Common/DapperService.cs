using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using SocietyLedger.Infrastructure.Data;
using System.Data;

namespace SocietyLedger.Infrastructure.Services.Common
{
    /// <summary>
    /// Centralises every raw Dapper operation in the application.
    /// All SQL execution must go through this service — service classes must
    /// never import Dapper or open NpgsqlConnections directly.
    /// </summary>
    public sealed class DapperService : IDapperService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly ILogger<DapperService> _logger;

        public DapperService(IDbConnectionFactory connectionFactory, ILogger<DapperService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger            = logger;
        }

        // ── Connection-less helpers ──────────────────────────────────────

        /// <summary>
        /// Executes a query and returns results.
        /// </summary>
        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null)
        {
            try
            {
                await using var conn = OpenConnection();
                await conn.OpenAsync();
                return await conn.QueryAsync<T>(sql, param);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QueryAsync failed. SQL: {Sql}", sql);
                throw;
            }
        }

        /// <summary>
        /// Executes a query and returns the first or default result.
        /// </summary>
        public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null)
        {
            try
            {
                await using var conn = OpenConnection();
                await conn.OpenAsync();
                return await conn.QueryFirstOrDefaultAsync<T>(sql, param);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QueryFirstOrDefaultAsync failed. SQL: {Sql}", sql);
                throw;
            }
        }

        /// <summary>
        /// Executes a command and returns affected rows.
        /// </summary>
        public async Task<int> ExecuteAsync(string sql, object? param = null)
        {
            try
            {
                await using var conn = OpenConnection();
                await conn.OpenAsync();
                return await conn.ExecuteAsync(sql, param);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteAsync failed. SQL: {Sql}", sql);
                throw;
            }
        }

        /// <summary>
        /// Executes a scalar command and returns the result.
        /// </summary>
        public async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null)
        {
            try
            {
                await using var conn = OpenConnection();
                await conn.OpenAsync();
                return await conn.ExecuteScalarAsync<T>(sql, param);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteScalarAsync failed. SQL: {Sql}", sql);
                throw;
            }
        }

        // ── Transactional helpers ────────────────────────────────────────

        /// <summary>
        /// Begins a transaction with specified isolation level.
        /// </summary>
        public async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> BeginTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.RepeatableRead)
        {
            var conn = OpenConnection();
            await conn.OpenAsync();
            var tx = await conn.BeginTransactionAsync(isolationLevel);
            return (conn, tx);
        }

        /// <summary>
        /// Executes a query within a transaction.
        /// </summary>
        public async Task<IEnumerable<T>> QueryAsync<T>(
            NpgsqlConnection conn, NpgsqlTransaction tx, string sql, object? param = null)
        {
            try
            {
                return await conn.QueryAsync<T>(sql, param, tx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QueryAsync (tx) failed. SQL: {Sql}", sql);
                throw;
            }
        }

        /// <summary>
        /// Executes a query within a transaction and returns the first or default result.
        /// </summary>
        public async Task<T?> QueryFirstOrDefaultAsync<T>(
            NpgsqlConnection conn, NpgsqlTransaction tx, string sql, object? param = null)
        {
            try
            {
                return await conn.QueryFirstOrDefaultAsync<T>(sql, param, tx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QueryFirstOrDefaultAsync (tx) failed. SQL: {Sql}", sql);
                throw;
            }
        }

        /// <summary>
        /// Executes a command within a transaction and returns affected rows.
        /// </summary>
        public async Task<int> ExecuteAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx, string sql, object? param = null)
        {
            try
            {
                return await conn.ExecuteAsync(sql, param, tx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteAsync (tx) failed. SQL: {Sql}", sql);
                throw;
            }
        }

        /// <summary>
        /// Executes a scalar command within a transaction and returns the result.
        /// </summary>
        public async Task<T?> ExecuteScalarAsync<T>(
            NpgsqlConnection conn, NpgsqlTransaction tx, string sql, object? param = null)
        {
            try
            {
                return await conn.ExecuteScalarAsync<T>(sql, param, tx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteScalarAsync (tx) failed. SQL: {Sql}", sql);
                throw;
            }
        }

        // ── Private ──────────────────────────────────────────────────────

        private NpgsqlConnection OpenConnection()
            => (NpgsqlConnection)_connectionFactory.CreateConnection();
    }
}
