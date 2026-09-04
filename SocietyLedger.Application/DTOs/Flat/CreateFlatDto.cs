using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SocietyLedger.Application.DTOs.Flat
{
    public record CreateFlatDto
    {
        public string FlatNo { get; set; } = string.Empty;
        public string? OwnerName { get; set; }
        public string? ContactMobile { get; set; }
        public string? ContactEmail { get; set; }
        public string? TenantName { get; set; }
        public string? TenantMobile { get; set; }
        public string? TenantEmail { get; set; }
        public decimal? MaintenanceAmount { get; set; }
        public string? StatusCode { get; set; }

        [JsonConstructor]
        public CreateFlatDto(
            string flatNo,
            string? ownerName,
            string? contactMobile,
            string? contactEmail,
            string? tenantName,
            string? tenantMobile,
            string? tenantEmail,
            decimal? maintenanceAmount,
            string? statusCode)
        {
            FlatNo = flatNo;
            OwnerName = ownerName;
            ContactMobile = contactMobile;
            ContactEmail = contactEmail;
            TenantName = tenantName;
            TenantMobile = tenantMobile;
            TenantEmail = tenantEmail;
            MaintenanceAmount = maintenanceAmount;
            StatusCode = statusCode;
        }

        public CreateFlatDto()
            : this(string.Empty, null, null, null, null, null, null, null, null)
        {
        }

        public CreateFlatDto(
            string flatNo,
            string? ownerName,
            string? contactMobile,
            string? contactEmail,
            decimal? maintenanceAmount,
            string? statusCode)
            : this(flatNo, ownerName, contactMobile, contactEmail, null, null, null, maintenanceAmount, statusCode)
        {
        }
    }
}
