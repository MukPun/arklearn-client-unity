using System.Runtime.InteropServices;
using XLua;

namespace XLua.LuaDLL
{ 
    public partial class Lua
    { 

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int luaopen_client_crypt(System.IntPtr L);

        [MonoPInvokeCallback(typeof(XLua.LuaDLL.lua_CSFunction))]
        public static int LoadCrypt(System.IntPtr L)
        {
            return luaopen_client_crypt(L);
        }
    }
}