namespace DeskBox.Tests;

public sealed class WidgetSegmentedRestoreLifecycleContractTests
{
    [Theory]
    [InlineData("TodoWidgetContentAdapter", "todoContent")]
    [InlineData("QuickCaptureWidgetContentAdapter", "content")]
    public void Adapters_ForwardVisibilityWithoutMaterializingAView(string adapter, string localName)
    {
        string source = Read($"{adapter}.cs");
        string visibility = Method(source, "public override void OnWindowVisibilityChanged(bool visible)");

        Assert.Contains("MaterializedView is", visibility, StringComparison.Ordinal);
        Assert.Contains($"{localName}.OnWindowVisibilityChanged(visible);", visibility, StringComparison.Ordinal);
        Assert.DoesNotContain("AsContent(View)", visibility, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TodoWidgetContent", "internal void OnWindowVisibilityChanged(bool visible)", "CancelTodoSegmentedRestore", "QueueTodoSegmentedRestore")]
    [InlineData("QuickCaptureSurfaceContent", "internal async void OnWindowVisibilityChanged(bool visible)", "CancelSegmentedRestore", "QueueSegmentedRestore")]
    public void VisibilityOwnsCancellationAndRequeue(
        string content,
        string signature,
        string cancelMethod,
        string queueMethod)
    {
        string visibility = Method(Read($"{content}.xaml.cs"), signature);

        Assert.Contains("SetHostVisibility(visible)", visibility, StringComparison.Ordinal);
        Assert.Contains($"{cancelMethod}();", visibility, StringComparison.Ordinal);
        Assert.Contains($"{queueMethod}();", visibility, StringComparison.Ordinal);
        int awaitIndex = visibility.IndexOf("await ", StringComparison.Ordinal);
        Assert.True(awaitIndex < 0 || visibility.IndexOf($"{cancelMethod}();", StringComparison.Ordinal) < awaitIndex);
    }

    [Theory]
    [InlineData("TodoWidgetContent", "TodoSegmentedRestoreSlot_SizeChanged", "ListHeaderArea", "RootGrid")]
    [InlineData("QuickCaptureSurfaceContent", "SegmentedRestoreSlot_SizeChanged", "ListPage", "ResponsiveContentGrid")]
    public void LayoutRetries_AreDrivenByTheActualCandidateSlots(
        string content,
        string handler,
        string innerSlot,
        string outerSlot)
    {
        string source = Read($"{content}.xaml.cs");

        Assert.Contains($"{innerSlot}.SizeChanged += {handler};", source, StringComparison.Ordinal);
        string slotHandler = Method(source, $"private void {handler}(");
        Assert.Contains("NotifyLayoutChanged()", slotHandler, StringComparison.Ordinal);
        Assert.Contains($"Math.Min({innerSlot}.ActualWidth, {outerSlot}.ActualWidth)", source, StringComparison.Ordinal);
        Assert.Contains("WidgetSegmentedRestoreFrameResult.RetryAfterLayoutChange", source, StringComparison.Ordinal);
        Assert.Contains("Microsoft.UI.Dispatching.DispatcherQueuePriority.Low", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoUnload_DiscardsInterruptedAttachmentTransitionsWithoutRunningLayout()
    {
        string source = Read("TodoWidgetContent.xaml.cs");
        string unloaded = Method(source, "private void TodoWidgetContent_Unloaded(");

        Assert.Contains("CancelTodoSegmentedRestore();", unloaded, StringComparison.Ordinal);
        Assert.Contains("_isInteractiveResizeActive = false;", unloaded, StringComparison.Ordinal);
        Assert.Contains("_isResponsiveLayoutTransitionActive = false;", unloaded, StringComparison.Ordinal);
        Assert.Contains("_segmentedLayoutRefreshPending = false;", unloaded, StringComparison.Ordinal);
        Assert.DoesNotContain("FinishResponsiveLayoutTransition();", unloaded, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyMasterDetailLayout(", unloaded, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueTodoSegmentedRestore();", unloaded, StringComparison.Ordinal);
    }

    private static string Read(string file) => File.ReadAllText(TestPaths.FromRepository(
        $"src/DeskBox/Controls/WidgetContents/{file}"));

    private static string Method(string source, string signature)
    {
        int signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureStart >= 0, $"Missing method: {signature}");
        int bodyStart = source.IndexOf('{', signatureStart);
        Assert.True(bodyStart >= 0);
        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[bodyStart..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Unclosed method: {signature}");
    }
}
