using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlatEntity = SocietyLedger.Domain.Entities.Flat;

namespace SocietyLedger.Application.DTOs.Flat
{
    public record FlatResponseDto(
     Guid PublicId,
     Guid SocietyPublicId,
     string FlatNo,
     string? OwnerName,
     string? ContactMobile,
     string? ContactEmail,
     string? TenantName,
     string? TenantMobile,
     string? TenantEmail,
     decimal MaintenanceAmount,
     short? StatusId,
     string StatusName,
     DateTime CreatedAt,
     DateTime UpdatedAt
 )
    {
        public decimal TotalOutstanding { get; init; } = 0m;

        /// <summary>
        /// Creates a FlatResponseDto from a Flat domain entity.
        /// </summary>
        public FlatResponseDto(FlatEntity flat) : this(
            PublicId: flat.PublicId,
            SocietyPublicId: flat.SocietyPublicId,
            FlatNo: flat.FlatNo,
            OwnerName: flat.OwnerName,
            ContactMobile: flat.ContactMobile,
            ContactEmail: flat.ContactEmail,
            TenantName: flat.TenantName,
            TenantMobile: flat.TenantMobile,
            TenantEmail: flat.TenantEmail,
            MaintenanceAmount: flat.MaintenanceAmount,
            StatusId: flat.StatusId,
            StatusName: flat.StatusName ?? "Unknown",
            CreatedAt: flat.CreatedAt,
            UpdatedAt: flat.UpdatedAt
        )
        {
        }
    }
}
