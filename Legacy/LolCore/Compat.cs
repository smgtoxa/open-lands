// netstandard2.1 lacks the marker type that C# 9 `init` accessors (records) need.
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
