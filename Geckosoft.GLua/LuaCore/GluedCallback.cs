using System;
using System.Collections.Generic;
using System.Reflection;
using Tao.Lua;
using System.Linq;

namespace Geckosoft.Scripting.LuaCore
{
    public class GluedCallback
    {
        public MethodInfo Method;
        public Object Instance = null;
		public List<GluedCallback> Overloads = new List<GluedCallback>();

        public GluedCallback(MethodInfo method)
        {
            Method = method;
        }

        public GluedCallback(MethodInfo method, Object instance)
        {
            Method = method;
            Instance = instance;
        }

		public static GluedCallback Wrap(MethodInfo method)
        {
            return new GluedCallback(method);
        }

		public static GluedCallback Wrap(MethodInfo method, Object instance)
		{
			return new GluedCallback(method, instance);
        }

		static bool IsParams(ParameterInfo param)
		{
			return param.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0;
		}

		private int OnCallOverloads(GLua vm)
		{
			int argc = Lua.lua_gettop(vm);

			foreach (var cb in Overloads.Where(cb => cb.CanCall(vm, argc)))
			{
				return cb.OnCall(vm);
			}

			return OnCall(vm, true);
		}

		bool CanCall(GLua vm, int count)
		{
			if (Method.GetParameters().Length != count) // fixme 'params' support
				return false;

			for (int i = 0; i < count; i++)
			{
				var param = Method.GetParameters()[i];

				var luaParamType = Lua.lua_type(vm, i+1);
				switch (luaParamType)
				{
					case Lua.LUA_TNUMBER:
						if (param.ParameterType != typeof(int)
							&& param.ParameterType != typeof(uint)
							&& param.ParameterType != typeof(short)
							&& param.ParameterType != typeof(ushort)
							&& param.ParameterType != typeof(ulong)
							&& param.ParameterType != typeof(float)
							&& param.ParameterType != typeof(double)
							&& param.ParameterType != typeof(decimal)
							&& param.ParameterType != typeof(sbyte)
							&& param.ParameterType != typeof(byte)
							&& !param.ParameterType.IsEnum)
							return false;
						break;
					case Lua.LUA_TSTRING:
						if (param.ParameterType != typeof(string))
							return false;
						break;
					case Lua.LUA_TBOOLEAN:
						if (param.ParameterType != typeof(bool))
							return false;
						break;
					case Lua.LUA_TUSERDATA:
						if (vm.DataStore.GetObject(i+1) == null || param.ParameterType != vm.DataStore.GetObject(i+1).GetType())
							return false;
						break;
				}

			}
			return true;
		}

		internal int OnCall(IntPtr L) { return OnCall(L, false); }

    	internal int OnCall(IntPtr L, bool force)
        {
            var VM = GLua.GetInstance(L);
        	var obj = Instance;

			if (VM == null)
				throw new Exception("VM not valid anymore");

			if (Overloads.Count > 0 && !force)
			{
				return OnCallOverloads(VM);
			} 

            IntPtr realL = L;

            realL = VM;

            try
            {
                VM.LuaStack = L; /* work around coroutines */   

                int argc = Lua.lua_gettop(L);
                var pars = Method.GetParameters();

                if (argc > pars.Length && (Method.IsStatic  || argc - 1 > pars.Length))
                    throw new Exception("Invalid parameter count. Got " + argc + " expected max " + pars.Length);

                var callingParams = new List<object>();

                int n = 1;

				if (argc > 0 && !Method.IsStatic)
				{
					var entry = VM.DataStore.GetEntry(n).Object;
					obj = entry;
				}

                foreach (var par in pars)
                {
                    // Special case - if it expects a GLua, hack in the param ;)
                    // Cannot be specified from lua !
                    if (par.ParameterType == typeof (GLua))
                    {
                        callingParams.Add(VM);
                        continue;
                    }

                    if (par.ParameterType == typeof (IntPtr)) // Assume this function wants the Lua pointer
                    {
                        callingParams.Add(L);
                        continue;
                    }

                    if (n == argc + 1)
                    {
                        // Skip additional ones
						if (!IsParams(par))
							callingParams.Add(null);
                        continue;
                    }

                    if (Lua.lua_isnil(L, n))
                    {
                        callingParams.Add(null);
                    }
                    else if (par.ParameterType == typeof (string))
                    {
                        callingParams.Add(Lua.lua_tostring(L, n));
                    }
                    else if (par.ParameterType == typeof (int))
                    {
                        callingParams.Add(Lua.lua_tointeger(L, n));
                    }
                    else if (par.ParameterType == typeof (double))
                    {
                        callingParams.Add(Lua.lua_tonumber(L, n));
                    }
                    else if (par.ParameterType == typeof (float))
                    {
                        callingParams.Add((float) Lua.lua_tonumber(L, n));
                    }
                    else if (par.ParameterType == typeof (bool))
                    {
                        callingParams.Add(Lua.lua_toboolean(L, n) == 1);
					}/*
					else if (par.ParameterType == typeof(IGluaObject))
					{
						callingParams.Add((IGluaObject)VM.DataStore.GetEntry(n));
					}*/
					else if (par.ParameterType == typeof(GluedData))
					{
						var entry = VM.DataStore.GetEntry(n);
						callingParams.Add(entry);
					}
                    else /* final chance */
                    {
						var entry = VM.DataStore.GetObject(n);

						callingParams.Add(entry);
                    }/*
                    else
                    {
                        callingParams.Add(Lua.lua_tostring(L, n)); // Shall return null
                    }*/

                    n++;
                }

				var result = Method.Invoke(obj, callingParams.ToArray());
                int resCount = 0;

                if (result != null && result.GetType().IsArray)
                {
                    var a = (Array) result;
                    /* push a table */
                    Lua.lua_newtable(L);

                    for (int i = 0; i < a.GetLength(0); i++)
                    {
                        var r = a.GetValue(i);
                        Lua.lua_pushinteger(L, i + 1); /* add the index */
                        PushResult(VM, r); /* add the value */
                        Lua.lua_settable(L, -3);
                    }
                    resCount++;
                }
                else
                {
                    PushResult(VM, result);
                    resCount++;
                }

                VM.LuaStack = realL;

                return resCount; // number of return values
            }catch(Exception ex)
            {
            	throw ex;
            }
			finally
			{
				if (VM != null)
					VM.LuaStack = realL;
			}
			return 0;
        }

    	protected void PushResult(GLua vm, object result)
        {
            if (result is string)
            {
                Lua.lua_pushstring(vm, (string)result);
            }
            else if (result is bool)
            {
                if ((bool)result)
                    Lua.lua_pushboolean(vm, 1);
                else
                    Lua.lua_pushboolean(vm, 0);
            }
            else if (result is int)
            {
                Lua.lua_pushinteger(vm, (int)result);
            }
            else if (result is double)
            {
                Lua.lua_pushnumber(vm, (double)result);
            }
            else if (result is float)
            {
                Lua.lua_pushnumber(vm, (float)result);
            }
            else if (result is GluedData)
            {
				var ud = result as GluedData;

                if (!ud.Created)
                {
                    ud.Inject(vm);
                }
            }
            else if (result is IGLuaObject)
            {
				vm.DataStore.Create(result, ((IGLuaObject)result).ObjectType);
			}
            else if (result != null)
            {
				vm.DataStore.Create(result, result.GetType().FullName);
            }
        }
    }
}
