using System.Reflection;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Pins the round-15 regression found on the round-14 restore-cancel fix: the
/// abort flag is raised before the gate is touched, so a token that is already
/// cancelled threw out of the gate wait and left the flag raised forever. The
/// next apply then silently discarded a staging payload the user never
/// cancelled.
/// </summary>
public sealed class RoundFifteenRegressionContractTests
{
    [Fact]
    public async Task RestoreCancel_AlreadyCancelledToken_DoesNotPoisonTheNextApply()
    {
        string root = Path.Combine(Path.GetTempPath(), "deskbox-r15-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new DeskBoxDataBackupService(root);
            FieldInfo? flag = typeof(DeskBoxDataBackupService).GetField(
                "_restoreCancelRequested",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(flag);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.CancelPendingRestoreAsync(new CancellationToken(canceled: true)));

            Assert.False(
                (bool)flag!.GetValue(service)!,
                "a cancel that aborted before reaching an apply must not leave the abort flag raised");

            DeskBoxRestoreApplyResult result = await service.ApplyPendingRestoreAsync();
            Assert.Equal(DeskBoxRestoreApplyResult.NoPendingRestore, result);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
