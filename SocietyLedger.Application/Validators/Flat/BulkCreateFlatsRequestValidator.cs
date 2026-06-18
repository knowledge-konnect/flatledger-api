using FluentValidation;
using SocietyLedger.Application.DTOs.Flat;
using SocietyLedger.Domain.Constants;

namespace SocietyLedger.Application.Validators.Flat
{
    /// <summary>
    /// Top-level validator for <see cref="BulkCreateFlatsRequest"/>.
    /// Validates the list bounds and delegates per-item format validation to
    /// <see cref="BulkCreateFlatItemDtoValidator"/> so format errors are caught
    /// before the service layer runs any DB queries.
    /// </summary>
    public class BulkCreateFlatsRequestValidator : AbstractValidator<BulkCreateFlatsRequest>
    {
        public BulkCreateFlatsRequestValidator()
        {
            RuleFor(x => x.Flats)
                .NotNull().WithMessage("Flats list is required.")
                .Must(f => f.Count > 0).WithMessage("Flats list cannot be empty.")
                .Must(f => f.Count <= ValidationPatterns.MaxBulkFlats)
                    .WithMessage($"Bulk create is limited to {ValidationPatterns.MaxBulkFlats} flats per request.");

            // Validate each item's format. Business-rule checks (duplicates, DB lookups)
            // remain in the service layer so they can return per-item failures rather than
            // aborting the whole batch.
            RuleForEach(x => x.Flats)
                .NotNull().WithMessage("Flat item cannot be null.")
                .SetValidator(new BulkCreateFlatItemDtoValidator());
        }
    }
}
