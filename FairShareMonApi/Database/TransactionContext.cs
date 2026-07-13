namespace FairShareMonApi.Database;

/// <summary>
/// Controls the outcome of an <c>ExecuteTransactionAsync</c> block: call <see cref="NoCommit"/>
/// inside the delegate to abort on validation/business failure - the transaction is rolled back
/// and nothing is saved.
/// </summary>
public class TransactionContext
{
    public bool ShouldCommit { get; private set; } = true;

    public void NoCommit() => ShouldCommit = false;
}
