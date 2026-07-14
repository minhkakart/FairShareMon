using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Admin;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>Revoke rule (M11 OQ4): chỉ giới hạn độ dài ghi chú tùy chọn.</summary>
public class RevokeTierRequestValidator : AbstractValidator<RevokeTierRequest>
{
    public RevokeTierRequestValidator()
    {
        RuleFor(request => request.Note)
            .MaximumLength(TierGrant.NoteMaxLength)
            .WithMessage($"Ghi chú không được vượt quá {TierGrant.NoteMaxLength} ký tự.");
    }
}
