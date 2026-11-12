using System;
using MoonSharp.Interpreter;

namespace RobloxKeeper.Tests
{
    // The interpreter itself: packed into the exe, found from memory, and
    // run with nothing that reaches files or programs.
    static class LuaEngineTests
    {
        public static void TestTheInterpreterIsPackedAndLoads()
        {
            Assert.True(LuaRuntime.Packed, "MoonSharp is inside the exe");
            Script s = new Script(CoreModules.Preset_SoftSandbox);
            Assert.Equal(7.0, s.DoString("return 3 + 4").Number, "it runs Lua");
        }
    }
}
