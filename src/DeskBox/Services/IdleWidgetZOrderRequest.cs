namespace DeskBox.Services;

/// <summary>
/// Coalesces peer-order work without losing a required capsule activation or
/// collapse repair to an idle request, animation retry, or interaction hold.
/// Owned by the UI thread.
/// </summary>
internal sealed class IdleWidgetZOrderRequest
{
    public long Generation { get; private set; }
    public bool RequiresPeerOrderRestore { get; private set; }

    public long Queue(bool requirePeerOrderRestore)
    {
        RequiresPeerOrderRestore |= requirePeerOrderRestore;
        return ++Generation;
    }

    // A new expansion invalidates callbacks, but its eventual release must
    // still repair the peer order left by the previous expansion.
    public void Invalidate() => Generation++;

    public bool Complete(long generation)
    {
        if (generation != Generation)
        {
            return false;
        }

        RequiresPeerOrderRestore = false;
        return true;
    }
}
