using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;

namespace SocietyLedger.Infrastructure.Persistence.Extensions
{
    public static class DbUpdateExceptionExtensions
    {
        public static bool IsUniqueConstraintViolation(this DbUpdateException ex)
        {
            if (ex.InnerException is PostgresException pgEx)
                return pgEx.SqlState == PostgresErrorCodes.UniqueViolation;

            return ex.InnerException?.Message.Contains("23505", StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
