using System.Runtime.CompilerServices;

namespace Core.Services
{
    public static class ReflectionService
    {
        public static string GetMethodName([CallerMemberName] string name = "") => name;
    }
}
