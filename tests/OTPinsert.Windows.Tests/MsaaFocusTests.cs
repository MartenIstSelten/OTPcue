using System.Reflection;
using System.Windows;
using Accessibility;
using OTPinsert.Platform;
using Xunit;

public sealed class MsaaFocusTests
{
    [Theory]
    [InlineData(42, 0x100004, true)] // Focused editable text.
    [InlineData(42, 0x100000, false)] // Focusable but not focused.
    [InlineData(42, 0x100005, false)] // Disabled.
    [InlineData(42, 0x100044, false)] // Read-only.
    [InlineData(42, 0x108004, false)] // Invisible.
    [InlineData(42, 0x110004, false)] // Offscreen.
    [InlineData(42, 0x20100004, false)] // Password/protected.
    [InlineData(9, 0x100004, false)] // Window, despite having focus.
    [InlineData(43, 0x100004, false)] // Button.
    public void OnlyFocusedEditableTextIsAccepted(int role, int state, bool expected)
        => Assert.Equal(expected, MsaaField.IsEditable(role, state));

    [Fact]
    public void FocusTraversalFollowsProviderObjectsAndSimpleChildIds()
    {
        var (root, rootStub) = CreateProvider();
        var (child, childStub) = CreateProvider();
        rootStub.Focus = child;
        childStub.Focus = 7;

        var field = MsaaField.Focused(root);

        Assert.NotNull(field);
        Assert.True(field.TryDescribe(out _, out _));
        Assert.Equal(7, childStub.LastChildId);
        Assert.True(field.SameAs(new MsaaField(child, 7)));
        Assert.False(field.SameAs(new MsaaField(child, 0)));
    }

    [Fact]
    public void MissingFocusDoesNotBecomeAField()
    {
        var (root, stub) = CreateProvider();
        stub.Focus = null;
        Assert.Null(MsaaField.Focused(root));
    }

    [Fact]
    public void CyclicFocusProviderIsBounded()
    {
        var (root, rootStub) = CreateProvider();
        var (child, childStub) = CreateProvider();
        rootStub.Focus = child;
        childStub.Focus = root;

        Assert.Null(MsaaField.Focused(root));
        Assert.Equal(MsaaNative.MaximumTraversalDepth, rootStub.FocusReads + childStub.FocusReads);
    }

    [Fact]
    public void SelfFocusDoesNotLoop()
    {
        var (root, stub) = CreateProvider();
        stub.Focus = root;
        Assert.NotNull(MsaaField.Focused(root));
        Assert.Equal(1, stub.FocusReads);
    }

    [Theory]
    [InlineData(0, 44, false)]
    [InlineData(284, 1, false)]
    [InlineData(-1, 44, false)]
    [InlineData(2, 2, true)]
    [InlineData(284, 44, true)]
    public void BoundsMustDescribeAVisibleField(int width, int height, bool expected)
    {
        var (root, stub) = CreateProvider();
        stub.Width = width;
        stub.Height = height;
        Assert.Equal(expected, new MsaaField(root, 0).TryDescribe(out _, out _));
    }

    [Fact]
    public void RevalidationRejectsAFieldThatBecomesProtected()
    {
        var (root, stub) = CreateProvider();
        var field = new MsaaField(root, 0);
        Assert.True(field.TryDescribe(out _, out _));
        stub.State |= 0x20000000;
        Assert.False(field.TryDescribe(out _, out _));
    }

    [Fact]
    public void MatchingFieldStillRequiresTheSameForegroundWindow()
    {
        var (root, _) = CreateProvider();
        var first = new FocusTarget(new MsaaField(root, 0), new Rect(0, 0, 284, 44), new IntPtr(1), "Code");
        var moved = new FocusTarget(new MsaaField(root, 0), new Rect(10, 10, 284, 44), new IntPtr(1), "Code");
        var otherWindow = new FocusTarget(new MsaaField(root, 0), moved.Bounds, new IntPtr(2), "Code");

        Assert.True(first.SameField(moved));
        Assert.False(first.SameField(otherWindow));
        Assert.Null(first.Element);
    }

    private static (IAccessible Accessible, AccessibleStub Stub) CreateProvider()
    {
        var provider = DispatchProxy.Create<IAccessible, AccessibleStub>();
        return (provider, (AccessibleStub)provider);
    }

    // Any attempt to read/write a field value fails: detection needs metadata only.
    public class AccessibleStub : DispatchProxy
    {
        internal object? Focus { get; set; } = 0;
        internal int FocusReads { get; private set; }
        internal int LastChildId { get; private set; }
        internal int State { get; set; } = 0x100004;
        internal int Width { get; set; } = 284;
        internal int Height { get; set; } = 44;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method?.Name)
            {
                case "get_accFocus":
                    FocusReads++;
                    return Focus;
                case "get_accRole":
                    LastChildId = (int)arguments![0]!;
                    return 42;
                case "get_accState":
                    return State;
                case "get_accName":
                    return "Verification code";
                case "accLocation":
                    arguments![0] = 10;
                    arguments[1] = 20;
                    arguments[2] = Width;
                    arguments[3] = Height;
                    return null;
                default:
                    throw new NotSupportedException(method?.Name);
            }
        }
    }
}
