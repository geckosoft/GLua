using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Tao.Lua;

namespace Geckosoft.Scripting.LuaCore
{
	public class GluedData
	{
		public Object Object;
        public ulong Index;
        public IntPtr Pointer;
        public bool Created = false;
        public string ObjectType = "";

        public GluedData()
        {
            
        }

		public GluedData(Object obj, ulong index)
        {
            Object = obj;
            Index = index;
        }

        internal void Inject(GLua vm)
        {
            if (ObjectType != "")
            {
                var ptr = Lua.lua_newuserdata(vm, 8);
                Marshal.Copy(BitConverter.GetBytes(Index), 0, ptr, 8);
                Pointer = ptr;

                Lua.luaL_getmetatable(vm, ObjectType);
                Lua.lua_setmetatable(vm, -2);
            }else
            {
                var ptr = Lua.lua_newuserdata(vm, 8);
                Marshal.Copy(BitConverter.GetBytes(Index), 0, ptr, 8);
                Pointer = ptr;
            }

            Created = true;
        }
	}
}
