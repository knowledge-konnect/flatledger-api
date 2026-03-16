using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocietyLedger.Shared
{
    public static class RouteGroupNames
    {
        public const string AUTHENTICATION = "Authentication";
        public const string USER = "User";
        public const string SOCIETY = "Society";
        public const string NOTIFICATION = "Notification";
        public const string FLAT = "Flat";
        public const string BILLING = "Billing";
        public const string EXPENSES = "Expenses";
        public const string MEMBERS = "Members";
        public const string SUBSCRIPTION = "Subscription";
        public const string INVOICE = "Invoice";
        public const string PLAN = "Plan";
        public const string PAYMENT = "Payment";
        public const string MAINTENANCE_PAYMENT = "Maintenance Payment";
        public const string PAYMENT_MODE = "Payment Mode";
        public const string OPENING_BALANCE = "Opening Balance";
        public const string REPORTS = "Reports";

        // SaaS Admin module
        public const string ADMIN_AUTH = "Admin - Authentication";
        public const string ADMIN_PLANS = "Admin - Plans";
        public const string ADMIN_SOCIETIES = "Admin - Societies";
        public const string ADMIN_SUBSCRIPTIONS = "Admin - Subscriptions";
        public const string ADMIN_PAYMENTS = "Admin - Payments";
        public const string ADMIN_FEATURES = "Admin - Feature Flags";
        public const string ADMIN_SETTINGS = "Admin - Platform Settings";
    }
}
