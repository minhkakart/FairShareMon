namespace FairShareMonApi.Constants;

/// <summary>
/// Strongly-typed resource keys for the <c>StringResources</c> resx family (neutral vi-VN + en-US),
/// resolved at runtime via <c>IStringLocalizer&lt;StringResources&gt;</c>. Grouped as
/// <c>Error.*</c> / <c>Validation.&lt;Area&gt;.*</c> / <c>Success.*</c> / <c>Envelope.*</c> /
/// <c>Serialization.*</c> per planning/localization-subsystem.md (D1).
/// </summary>
public static class MessageKeys
{
    public static class Error
    {
        public const string Unauthorized = "Error.Unauthorized";
        public const string Forbidden = "Error.Forbidden";
        public const string UsernameTaken = "Error.UsernameTaken";
        public const string InvalidCredentials = "Error.InvalidCredentials";
        public const string AccountDisabled = "Error.AccountDisabled";
        public const string InternalError = "Error.InternalError";
        public const string InvalidRefreshToken = "Error.InvalidRefreshToken";
        public const string CurrentPasswordIncorrect = "Error.CurrentPasswordIncorrect";
        public const string DefaultCategoryNotDeletable = "Error.DefaultCategoryNotDeletable";
        public const string CategoryNotFound = "Error.CategoryNotFound";
        public const string CategoryNameDuplicate = "Error.CategoryNameDuplicate";
        public const string EventClosed = "Error.EventClosed";
        public const string EventClosedEdit = "Error.EventClosedEdit";
        public const string EventClosedDelete = "Error.EventClosedDelete";
        public const string EventClosedDetach = "Error.EventClosedDetach";
        public const string EventRangeExcludesAssignedExpenses = "Error.EventRangeExcludesAssignedExpenses";
        public const string EventNotFound = "Error.EventNotFound";
        public const string ExpensePayerInvalid = "Error.ExpensePayerInvalid";
        public const string ExpenseCategoryInvalid = "Error.ExpenseCategoryInvalid";
        public const string ExpenseTagInvalid = "Error.ExpenseTagInvalid";
        public const string ShareMemberInvalid = "Error.ShareMemberInvalid";
        public const string DuplicateShareMember = "Error.DuplicateShareMember";
        public const string ExpenseTimeOutOfEventRange = "Error.ExpenseTimeOutOfEventRange";
        public const string ExpenseNotFound = "Error.ExpenseNotFound";
        public const string OwnerRepresentativeNotDeletable = "Error.OwnerRepresentativeNotDeletable";
        public const string MemberNotFound = "Error.MemberNotFound";
        public const string OwnerRepresentativeShareNotDeletable = "Error.OwnerRepresentativeShareNotDeletable";
        public const string OwnerRepresentativeShareMemberNotChangeable = "Error.OwnerRepresentativeShareMemberNotChangeable";
        public const string ShareNotFound = "Error.ShareNotFound";
        public const string MemberLimitReached = "Error.MemberLimitReached";
        public const string OpenEventLimitReached = "Error.OpenEventLimitReached";
        public const string MonthlyExpenseLimitReached = "Error.MonthlyExpenseLimitReached";
        public const string PremiumFeatureRequired = "Error.PremiumFeatureRequired";
        public const string EventNotClosedForQr = "Error.EventNotClosedForQr";
        public const string NoOutstandingDebtForQr = "Error.NoOutstandingDebtForQr";
        public const string BankAccountNotFound = "Error.BankAccountNotFound";
        public const string NoBankAccountForQr = "Error.NoBankAccountForQr";
        public const string AdminCannotTargetSelf = "Error.AdminCannotTargetSelf";
        public const string AdminCannotTargetAdmin = "Error.AdminCannotTargetAdmin";
        public const string AdminUserNotFound = "Error.AdminUserNotFound";
        public const string UnsupportedExportFormat = "Error.UnsupportedExportFormat";
        public const string TagNotFound = "Error.TagNotFound";
        public const string TagNameDuplicate = "Error.TagNameDuplicate";
    }

    public static class Feature
    {
        public const string Wallet = "Feature.Wallet";
        public const string Qr = "Feature.Qr";
    }

    public static class Qr
    {
        public static class Header
        {
            public const string Bank = "Qr.Header.Bank";
            public const string AccountHolder = "Qr.Header.AccountHolder";
            public const string AccountNumber = "Qr.Header.AccountNumber";
            public const string Amount = "Qr.Header.Amount";
        }
    }

    public static class Envelope
    {
        public const string ValidationFailed = "Envelope.ValidationFailed";
        public const string FieldInvalid = "Envelope.FieldInvalid";
        public const string InternalError = "Envelope.InternalError";
    }

    public static class Serialization
    {
        public const string DateTimeMustBeString = "Serialization.DateTimeMustBeString";
        public const string DateTimeInvalid = "Serialization.DateTimeInvalid";
        public const string DateTimeInvalidWithValue = "Serialization.DateTimeInvalidWithValue";
    }

    public static class Success
    {
        public const string AdminAccountDisabled = "Success.AdminAccountDisabled";
        public const string AdminAccountEnabled = "Success.AdminAccountEnabled";
        public const string AdminTokensRevoked = "Success.AdminTokensRevoked";
        public const string AdminRoleUpdated = "Success.AdminRoleUpdated";
        public const string LoggedOut = "Success.LoggedOut";
        public const string PasswordChanged = "Success.PasswordChanged";
        public const string BankAccountSetDefault = "Success.BankAccountSetDefault";
        public const string BankAccountDeleted = "Success.BankAccountDeleted";
        public const string CategorySetDefault = "Success.CategorySetDefault";
        public const string CategoryDeleted = "Success.CategoryDeleted";
        public const string EventDeleted = "Success.EventDeleted";
        public const string EventClosed = "Success.EventClosed";
        public const string ExpenseDeleted = "Success.ExpenseDeleted";
        public const string ExpenseSettledUpdated = "Success.ExpenseSettledUpdated";
        public const string ExpenseDetached = "Success.ExpenseDetached";
        public const string ShareDeleted = "Success.ShareDeleted";
        public const string ShareSettledUpdated = "Success.ShareSettledUpdated";
        public const string MemberSettledUpdated = "Success.MemberSettledUpdated";
        public const string HealthOk = "Success.HealthOk";
        public const string MemberDeleted = "Success.MemberDeleted";
        public const string TagDeleted = "Success.TagDeleted";
    }

    public static class Validation
    {
        public static class Auth
        {
            public const string CurrentPasswordRequired = "Validation.Auth.CurrentPasswordRequired";
            public const string NewPasswordRequired = "Validation.Auth.NewPasswordRequired";
            public const string NewPasswordTooShort = "Validation.Auth.NewPasswordTooShort";
            public const string NewPasswordTooLong = "Validation.Auth.NewPasswordTooLong";
            public const string UsernameRequired = "Validation.Auth.UsernameRequired";
            public const string PasswordRequired = "Validation.Auth.PasswordRequired";
            public const string RefreshTokenRequired = "Validation.Auth.RefreshTokenRequired";
            public const string UsernameLength = "Validation.Auth.UsernameLength";
            public const string UsernamePattern = "Validation.Auth.UsernamePattern";
            public const string PasswordTooShort = "Validation.Auth.PasswordTooShort";
            public const string PasswordTooLong = "Validation.Auth.PasswordTooLong";
        }

        public static class Member
        {
            public const string NameRequired = "Validation.Member.NameRequired";
            public const string NameTooLong = "Validation.Member.NameTooLong";
        }

        public static class Category
        {
            public const string NameRequired = "Validation.Category.NameRequired";
            public const string NameTooLong = "Validation.Category.NameTooLong";
            public const string ColorRequired = "Validation.Category.ColorRequired";
            public const string ColorInvalid = "Validation.Category.ColorInvalid";
            public const string IconTooLong = "Validation.Category.IconTooLong";
        }

        public static class Tag
        {
            public const string NameRequired = "Validation.Tag.NameRequired";
            public const string NameTooLong = "Validation.Tag.NameTooLong";
        }

        public static class Event
        {
            public const string NameRequired = "Validation.Event.NameRequired";
            public const string NameTooLong = "Validation.Event.NameTooLong";
            public const string DescriptionTooLong = "Validation.Event.DescriptionTooLong";
            public const string StartDateRequired = "Validation.Event.StartDateRequired";
            public const string EndDateRequired = "Validation.Event.EndDateRequired";
            public const string EndDateBeforeStart = "Validation.Event.EndDateBeforeStart";
        }

        public static class Expense
        {
            public const string NameRequired = "Validation.Expense.NameRequired";
            public const string NameTooLong = "Validation.Expense.NameTooLong";
            public const string DescriptionTooLong = "Validation.Expense.DescriptionTooLong";
            public const string ExpenseTimeRequired = "Validation.Expense.ExpenseTimeRequired";
            public const string EventUuidRequired = "Validation.Expense.EventUuidRequired";
        }

        public static class Share
        {
            public const string MemberRequired = "Validation.Share.MemberRequired";
        }

        public static class BankAccount
        {
            public const string BankBinRequired = "Validation.BankAccount.BankBinRequired";
            public const string BankBinPattern = "Validation.BankAccount.BankBinPattern";
            public const string BankNameRequired = "Validation.BankAccount.BankNameRequired";
            public const string BankNameTooLong = "Validation.BankAccount.BankNameTooLong";
            public const string AccountNumberRequired = "Validation.BankAccount.AccountNumberRequired";
            public const string AccountNumberInvalid = "Validation.BankAccount.AccountNumberInvalid";
            public const string AccountHolderNameRequired = "Validation.BankAccount.AccountHolderNameRequired";
            public const string AccountHolderNameTooLong = "Validation.BankAccount.AccountHolderNameTooLong";
        }

        public static class Common
        {
            public const string RangeInvalid = "Validation.Common.RangeInvalid";
            public const string AmountNegative = "Validation.Common.AmountNegative";
            public const string NoteTooLong = "Validation.Common.NoteTooLong";
        }

        public static class Stats
        {
            public const string ScopeConflict = "Validation.Stats.ScopeConflict";
        }

        public static class Admin
        {
            public const string BucketInvalid = "Validation.Admin.BucketInvalid";
            public const string PageMin = "Validation.Admin.PageMin";
            public const string PageSizeRange = "Validation.Admin.PageSizeRange";
            public const string SortInvalid = "Validation.Admin.SortInvalid";
            public const string DirectionInvalid = "Validation.Admin.DirectionInvalid";
            public const string CurrencyTooLong = "Validation.Admin.CurrencyTooLong";
            public const string ReferenceTooLong = "Validation.Admin.ReferenceTooLong";
            public const string RoleRequired = "Validation.Admin.RoleRequired";
            public const string RoleInvalid = "Validation.Admin.RoleInvalid";
        }

    }
}
