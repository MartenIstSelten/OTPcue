internal static class Program
{
    private static async Task Main()
    {
        var tests = new GoogleOfflineTests();
        tests.SignInErrorsAreActionableAndRedacted();
        tests.AuthorizationUsesFreshPkceAndNoClientSecret();
        tests.AccountConfigurationRoundtripsAndRejectsUnsafeEndpoints();
        tests.TokenMetadataAndProviderValuesRemainCompatible();
        await tests.InterruptedHistoryRetriesWithoutReplayingMessages();
        await tests.UnsuitableMessagesCannotProduceCodes();
        await tests.UnsuitableMetadataPreventsRawDownload();
        await tests.OversizedResponseDoesNotBlockFollowingCode("metadata");
        await tests.OversizedResponseDoesNotBlockFollowingCode("raw");
        await tests.UnderestimatedMessageSizeCannotProduceCode();
        await tests.InterruptedRawDownloadIsRetried();
        await tests.CancellationPreventsCursorCommit();
    }
}
