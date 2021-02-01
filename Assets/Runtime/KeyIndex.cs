using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct KeyIndex
{
    public int KeyHash;
    public int FirstIndex;
    public int ElementCount;
}
