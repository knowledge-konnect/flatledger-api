namespace SocietyLedger.Infrastructure.Services.Templates
{
    /// <summary>
    /// Inline-styled HTML email templates for FlatLedger transactional emails.
    /// All templates are self-contained and render correctly in major email clients.
    /// </summary>
    internal static class EmailTemplates
    {
        private const string BaseStyle = "font-family:Arial,Helvetica,sans-serif;background:#f4f6f9;margin:0;padding:0;";
        private const string CardStyle = "max-width:600px;margin:32px auto;background:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);";
        private const string HeaderStyle = "background:#1a56db;padding:32px 40px;text-align:center;";
        private const string BodyStyle = "padding:36px 40px;";
        private const string FooterStyle = "background:#f4f6f9;padding:24px 40px;text-align:center;border-top:1px solid #e5e7eb;";
        private const string BtnStyle = "display:inline-block;background:#1a56db;color:#ffffff;text-decoration:none;padding:14px 32px;border-radius:6px;font-size:15px;font-weight:600;margin:16px 0;";
        private const string InfoRowStyle = "padding:8px 0;border-bottom:1px solid #f0f0f0;";
        private const string LabelStyle = "color:#6b7280;font-size:13px;";
        private const string ValueStyle = "color:#111827;font-size:14px;font-weight:600;";

        private static string Wrap(string headerTitle, string bodyHtml, string supportEmail, string supportPhone) =>
            $@"<!DOCTYPE html><html lang=""en""><head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""><title>{headerTitle}</title></head>
<body style=""{BaseStyle}"">
  <div style=""{CardStyle}"">
    <div style=""{HeaderStyle}"">
      <h1 style=""color:#ffffff;margin:0;font-size:24px;letter-spacing:-0.5px;"">FlatLedger</h1>
      <p style=""color:#bfdbfe;margin:6px 0 0;font-size:13px;"">{headerTitle}</p>
    </div>
    <div style=""{BodyStyle}"">
      {bodyHtml}
    </div>
    <div style=""{FooterStyle}"">
      <p style=""color:#6b7280;font-size:12px;margin:0;"">Need help? Contact us at
        <a href=""mailto:{supportEmail}"" style=""color:#1a56db;text-decoration:none;"">{supportEmail}</a>
        {(string.IsNullOrWhiteSpace(supportPhone) ? "" : $" or call <strong>{supportPhone}</strong>")}
      </p>
      <p style=""color:#9ca3af;font-size:11px;margin:8px 0 0;"">&copy; {DateTime.UtcNow.Year} FlatLedger. All rights reserved.</p>
    </div>
  </div>
</body></html>";

        public static string PasswordReset(string userName, string resetLink, DateTime expiresAt, string supportEmail, string supportPhone = "")
        {
            var body = $@"
      <h2 style=""color:#111827;margin:0 0 8px;"">Password Reset Request</h2>
      <p style=""color:#374151;font-size:14px;line-height:1.6;"">Hi <strong>{EscapeHtml(userName)}</strong>,</p>
      <p style=""color:#374151;font-size:14px;line-height:1.6;"">We received a request to reset your FlatLedger password. Click the button below to choose a new password.</p>
      <p style=""text-align:center;""><a href=""{resetLink}"" style=""{BtnStyle}"">Reset Password</a></p>
      <p style=""color:#6b7280;font-size:13px;"">This link expires at <strong>{expiresAt:yyyy-MM-dd HH:mm} UTC</strong> and can only be used once.</p>
      <p style=""color:#6b7280;font-size:13px;"">If you didn't request a password reset, you can safely ignore this email. Your password will not change.</p>
      <hr style=""border:none;border-top:1px solid #e5e7eb;margin:24px 0;"">
      <p style=""color:#9ca3af;font-size:12px;"">For security, never share this link with anyone.</p>";

            return Wrap("Password Reset Request", body, supportEmail, supportPhone);
        }

        public static string Welcome(
            string adminName,
            string societyName,
            string planName,
            string trialOrExpiryDate,
            string loginUrl,
            string supportEmail,
            string supportPhone)
        {
            var body = $@"
      <h2 style=""color:#111827;margin:0 0 8px;"">Welcome to FlatLedger!</h2>
      <p style=""color:#374151;font-size:14px;line-height:1.6;"">Hi <strong>{EscapeHtml(adminName)}</strong>,</p>
      <p style=""color:#374151;font-size:14px;line-height:1.6;"">
        Congratulations on registering <strong>{EscapeHtml(societyName)}</strong>. Your account is ready and your free trial has started.
      </p>
      <table style=""width:100%;border-collapse:collapse;margin:20px 0;"">
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Society Name</td><td style=""{ValueStyle}"">{EscapeHtml(societyName)}</td></tr>
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Admin Name</td><td style=""{ValueStyle}"">{EscapeHtml(adminName)}</td></tr>
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Current Plan</td><td style=""{ValueStyle}"">{EscapeHtml(planName)}</td></tr>
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Trial / Subscription End</td><td style=""{ValueStyle}"">{EscapeHtml(trialOrExpiryDate)}</td></tr>
      </table>
      <p style=""text-align:center;""><a href=""{loginUrl}"" style=""{BtnStyle}"">Sign In to FlatLedger</a></p>
      <p style=""color:#374151;font-size:14px;line-height:1.6;"">
        Get started by adding your society's flats, configuring maintenance charges, and inviting members.
      </p>";

            return Wrap("Welcome to FlatLedger", body, supportEmail, supportPhone);
        }

        public static string SubscriptionExpiryReminder(
            string societyName,
            string planName,
            string expiryDate,
            string renewUrl,
            string supportEmail,
            string supportPhone,
            string stage)
        {
            var urgencyMsg = stage switch
            {
                "0d" => "⚠️ Your subscription <strong>expires today</strong>.",
                "1d" => "Your subscription expires <strong>tomorrow</strong>.",
                _ => "Your subscription expires in <strong>7 days</strong>."
            };

            var urgencyColor = stage == "0d" ? "#dc2626" : stage == "1d" ? "#d97706" : "#374151";

            var body = $@"
      <h2 style=""color:#111827;margin:0 0 8px;"">Subscription Expiry Reminder</h2>
      <p style=""color:{urgencyColor};font-size:14px;line-height:1.6;"">{urgencyMsg}</p>
      <table style=""width:100%;border-collapse:collapse;margin:20px 0;"">
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Society Name</td><td style=""{ValueStyle}"">{EscapeHtml(societyName)}</td></tr>
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Current Plan</td><td style=""{ValueStyle}"">{EscapeHtml(planName)}</td></tr>
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Subscription Expiry</td><td style=""{ValueStyle}"">{EscapeHtml(expiryDate)}</td></tr>
      </table>
      <p style=""color:#374151;font-size:14px;line-height:1.6;"">
        Renew now to keep uninterrupted access to your society's ledger, billing, and maintenance records.
      </p>
      <p style=""text-align:center;""><a href=""{renewUrl}"" style=""{BtnStyle}"">Renew Subscription</a></p>";

            return Wrap("Subscription Expiry Reminder", body, supportEmail, supportPhone);
        }

        public static string ContactUsNotification(
            string senderName,
            string senderEmail,
            string subject,
            string message,
            string supportEmail,
            string supportPhone = "")
        {
            var body = $@"
      <h2 style=""color:#111827;margin:0 0 8px;"">New Contact Us Submission</h2>
      <p style=""color:#374151;font-size:14px;"">A user has submitted the contact form on FlatLedger.</p>
      <table style=""width:100%;border-collapse:collapse;margin:20px 0;"">
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Name</td><td style=""{ValueStyle}"">{EscapeHtml(senderName)}</td></tr>
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Email</td><td style=""{ValueStyle}""><a href=""mailto:{EscapeHtml(senderEmail)}"" style=""color:#1a56db;"">{EscapeHtml(senderEmail)}</a></td></tr>
        <tr style=""{InfoRowStyle}""><td style=""{LabelStyle}"">Subject</td><td style=""{ValueStyle}"">{EscapeHtml(subject)}</td></tr>
      </table>
      <p style=""{LabelStyle}"">Message:</p>
      <div style=""background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;padding:16px;margin-top:8px;"">
        <p style=""color:#374151;font-size:14px;line-height:1.6;white-space:pre-wrap;margin:0;"">{EscapeHtml(message)}</p>
      </div>";

            return Wrap("New Contact Us Submission", body, supportEmail, supportPhone);
        }

        private static string EscapeHtml(string input) =>
            input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#x27;");
    }
}
