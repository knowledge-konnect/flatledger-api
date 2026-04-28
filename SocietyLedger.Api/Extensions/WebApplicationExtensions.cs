namespace SocietyLedger.Api.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="WebApplication"/> to keep Program.cs concise.
    /// </summary>
    public static class WebApplicationExtensions
    {
        /// <summary>
        /// Registers the global exception handling middleware.
        /// </summary>
        public static WebApplication UseCustomMiddleware(this WebApplication app)
        {
            app.UseMiddleware<SocietyLedger.Api.Middlewares.ExceptionMiddleware>();
            return app;
        }
    }
}
