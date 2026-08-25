using FairShareMonApi.Repositories;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for <see cref="EventSettlementClassifier.Classify"/> - the four-way
/// eligibility classification event-expense-settlement-sync Direction 1 gates on, per the planning
/// doc's Step M1.5 test list: <c>advanced==owed</c> → <c>NetZero</c>; <c>advanced&lt;owed</c> →
/// <c>NetDebtor</c> regardless of the gross-purity fact; <c>advanced&gt;owed</c> + no debtor-share
/// elsewhere → <c>NetCreditorGrossPure</c>; <c>advanced&gt;owed</c> + a debtor-share elsewhere (the
/// OQ-L worked example) → <c>NetCreditorGrossMixed</c>. Also covers <see cref="MemberSettlementFacts"/>'s
/// computed properties (<c>Balance</c>/<c>NetOwed</c>/<c>IsEligibleForDirection1Cascade</c>).
/// </summary>
public class EventSettlementClassifierTests
{
    [Fact]
    public void Classify_AdvancedEqualsOwed_ReturnsNetZero()
    {
        var result = EventSettlementClassifier.Classify(advanced: 500_000m, owed: 500_000m, hasDebtorShareElsewhere: false);

        Assert.Equal(MemberSettlementEligibility.NetZero, result);
    }

    [Fact]
    public void Classify_AdvancedEqualsOwed_HasDebtorShareElsewhere_StillReturnsNetZero()
    {
        // NetZero is decided purely by the SIGN of the balance - gross-purity is irrelevant once the
        // balance is exactly zero (the "mixed" residual bucket, per the planning doc's Assumptions).
        var result = EventSettlementClassifier.Classify(advanced: 500_000m, owed: 500_000m, hasDebtorShareElsewhere: true);

        Assert.Equal(MemberSettlementEligibility.NetZero, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classify_AdvancedLessThanOwed_ReturnsNetDebtor_RegardlessOfHasDebtorShareElsewhere(bool hasDebtorShareElsewhere)
    {
        var result = EventSettlementClassifier.Classify(advanced: 200_000m, owed: 500_000m, hasDebtorShareElsewhere);

        Assert.Equal(MemberSettlementEligibility.NetDebtor, result);
    }

    [Fact]
    public void Classify_AdvancedGreaterThanOwed_NoDebtorShareElsewhere_ReturnsNetCreditorGrossPure()
    {
        var result = EventSettlementClassifier.Classify(advanced: 800_000m, owed: 300_000m, hasDebtorShareElsewhere: false);

        Assert.Equal(MemberSettlementEligibility.NetCreditorGrossPure, result);
    }

    [Fact]
    public void Classify_AdvancedGreaterThanOwed_HasDebtorShareElsewhere_ReturnsNetCreditorGrossMixed()
    {
        // The OQ-L worked example's core algebra: net creditor overall, but holds a genuine debtor-share.
        var result = EventSettlementClassifier.Classify(advanced: 800_000m, owed: 300_000m, hasDebtorShareElsewhere: true);

        Assert.Equal(MemberSettlementEligibility.NetCreditorGrossMixed, result);
    }

    [Theory]
    [InlineData(MemberSettlementEligibility.NetDebtor, true)]
    [InlineData(MemberSettlementEligibility.NetCreditorGrossPure, true)]
    [InlineData(MemberSettlementEligibility.NetCreditorGrossMixed, false)]
    [InlineData(MemberSettlementEligibility.NetZero, false)]
    public void IsEligibleForDirection1Cascade_TrueOnlyForDebtorOrGrossPureCreditor(MemberSettlementEligibility eligibility, bool expected)
    {
        var facts = new MemberSettlementFacts(1, 0m, 0m, false, eligibility);

        Assert.Equal(expected, facts.IsEligibleForDirection1Cascade);
    }

    [Fact]
    public void MemberSettlementFacts_Balance_IsAdvancedMinusOwed()
    {
        var facts = new MemberSettlementFacts(1, 800_000m, 500_000m, false, MemberSettlementEligibility.NetCreditorGrossPure);

        Assert.Equal(300_000m, facts.Balance);
    }

    [Fact]
    public void MemberSettlementFacts_NetOwed_PositiveForDebtor_ZeroForCreditor()
    {
        var debtor = new MemberSettlementFacts(1, 200_000m, 500_000m, false, MemberSettlementEligibility.NetDebtor);
        var creditor = new MemberSettlementFacts(2, 800_000m, 500_000m, false, MemberSettlementEligibility.NetCreditorGrossPure);

        Assert.Equal(300_000m, debtor.NetOwed);
        Assert.Equal(0m, creditor.NetOwed);
    }

    [Fact]
    public void MemberSettlementFacts_NetOwed_ZeroForNetZero()
    {
        var netZero = new MemberSettlementFacts(1, 500_000m, 500_000m, false, MemberSettlementEligibility.NetZero);

        Assert.Equal(0m, netZero.NetOwed);
    }
}
