using System.Reflection;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the correlation-code format (OQ1, no DB) - planning/bank-callback-settlement.md
/// Step 10. Proves the format constants match the locked decision (a) and that the actual random-suffix
/// generator (<c>QrCorrelationCodeRepository.RandomSuffix</c>, invoked via reflection since it is a
/// private implementation detail with no DB dependency of its own) always produces alphabet-restricted,
/// correctly-sized, unambiguous output. The DB-dependent collision-retry/uniqueness behaviour of
/// <c>GenerateUniqueCodeAsync</c> itself is covered by the integration suite
/// (<c>QrCorrelationCodeRepositoryTests</c>).
/// </summary>
public class QrCorrelationCodeFormatTests
{
    // Visually-ambiguous characters the alphabet must exclude (OQ1: some bank apps show the memo back to the payer).
    private static readonly char[] AmbiguousCharacters = ['O', '0', 'I', '1', 'L'];

    private static readonly MethodInfo RandomSuffixMethod =
        typeof(QrCorrelationCodeRepository).GetMethod("RandomSuffix", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("QrCorrelationCodeRepository.RandomSuffix not found - has it been renamed?");

    private static string RandomSuffix(int length) => (string)RandomSuffixMethod.Invoke(null, [length])!;

    [Fact]
    public void CodePrefix_IsFsm()
    {
        Assert.Equal("FSM", QrCorrelationCode.CodePrefix);
    }

    [Fact]
    public void CodeRandomLength_IsSix()
    {
        Assert.Equal(6, QrCorrelationCode.CodeRandomLength);
    }

    [Fact]
    public void CodeMaxLength_HasRoomForPrefixPlusRandomSuffix()
    {
        Assert.True(QrCorrelationCode.CodeMaxLength >= QrCorrelationCode.CodePrefix.Length + QrCorrelationCode.CodeRandomLength);
    }

    [Fact]
    public void CodeAlphabet_ExcludesEveryVisuallyAmbiguousCharacter()
    {
        foreach (var ambiguous in AmbiguousCharacters)
            Assert.DoesNotContain(ambiguous, QrCorrelationCode.CodeAlphabet);
    }

    [Fact]
    public void CodeAlphabet_IsUppercaseOnlyWithNoDuplicates()
    {
        Assert.Equal(QrCorrelationCode.CodeAlphabet, QrCorrelationCode.CodeAlphabet.ToUpperInvariant());
        Assert.Equal(QrCorrelationCode.CodeAlphabet.Length, QrCorrelationCode.CodeAlphabet.Distinct().Count());
    }

    [Fact]
    public void CodeAlphabet_HasAtLeastThirtySymbols()
    {
        // OQ1: "a 30-symbol alphabet" - the collision-safety math (30^6) depends on this.
        Assert.True(QrCorrelationCode.CodeAlphabet.Length >= 30);
    }

    [Fact]
    public void RandomSuffix_AlwaysProducesConfiguredLength()
    {
        for (var i = 0; i < 200; i++)
            Assert.Equal(QrCorrelationCode.CodeRandomLength, RandomSuffix(QrCorrelationCode.CodeRandomLength).Length);
    }

    [Fact]
    public void RandomSuffix_EveryCharacterComesFromTheConfiguredAlphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var suffix = RandomSuffix(QrCorrelationCode.CodeRandomLength);
            foreach (var ch in suffix)
                Assert.Contains(ch, QrCorrelationCode.CodeAlphabet);
        }
    }

    [Fact]
    public void FullGeneratedCode_PrefixPlusSuffix_NeverContainsAmbiguousCharacters()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = QrCorrelationCode.CodePrefix + RandomSuffix(QrCorrelationCode.CodeRandomLength);
            foreach (var ambiguous in AmbiguousCharacters)
                Assert.DoesNotContain(ambiguous, code);
        }
    }

    [Fact]
    public void FullGeneratedCode_AlwaysStartsWithThePrefix()
    {
        var code = QrCorrelationCode.CodePrefix + RandomSuffix(QrCorrelationCode.CodeRandomLength);

        Assert.StartsWith(QrCorrelationCode.CodePrefix, code);
        Assert.Equal(QrCorrelationCode.CodePrefix.Length + QrCorrelationCode.CodeRandomLength, code.Length);
    }
}
