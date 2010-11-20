using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Linq;
using Geckosoft.Scripting.LuaCore;
using Tao.Lua;

namespace Geckosoft.Scripting
{
	public class GLua : IDisposable
	{
		public bool IsCoroutine = false;

		private static List<KeyValuePair<IntPtr, GLua>> _instances = new List<KeyValuePair<IntPtr, GLua>>();
		public List<GluedCallback> CallbackReferences = new List<GluedCallback>();
		public List<Lua.luaL_Reg> CallbackReferences2 = new List<Lua.luaL_Reg>();

		internal GluedDataStore DataStore;
		public bool HasVM
		{
			get { return (LuaStack.ToInt64() != 0); }
		}

		internal IntPtr LuaStack = (IntPtr) 0;

		protected GLua(IntPtr vm)
		{
			LuaStack = vm;
			DataStore = new GluedDataStore(this);
		}

		public static GLua New()
		{
			lock (_instances)
			{
				var vm = Lua.luaL_newstate();

				// Get a new glua instance
				var glua = new GLua(vm);

				return LinkVM(vm, glua);
			}
		}

		public static GLua GetInstance(IntPtr vm)
		{
			lock (_instances)
				return _instances.Where(kv => kv.Key == vm).Select(kv => kv.Value).FirstOrDefault();
		}

		protected static GLua LinkVM(IntPtr vm, GLua glua)
		{
			lock (glua)
			lock (_instances)
			{
				// Clean up
				_instances.RemoveAll(kv => kv.Key == vm);

				// link it
				_instances.Add(new KeyValuePair<IntPtr, GLua>(vm, glua));

				return glua;
			}
		}

		public void OpenLibs()
		{
			Lua.luaL_openlibs(LuaStack);
		}

		public void Destroy()
		{
			Dispose(true);
		}

		public static implicit operator IntPtr(GLua vm)
		{
			return vm.LuaStack;
		}

		public int Eval(string luacode)
		{
			int s = Lua.luaL_loadstring(this, luacode);

			if (s == 0)
			{
				// execute Lua program
				s = Lua.lua_pcall(this, 0, Lua.LUA_MULTRET, 0);
			}

			CheckStatus(s, false);

			return s;
		}

		public int Run(string path)
		{
			int s = Lua.luaL_loadfile(this, path);

			if (s == 0)
			{
				// execute Lua program
				s = Lua.lua_pcall(this, 0, Lua.LUA_MULTRET, 0);
			}

			return s;
		}


		static Lua.lua_CFunction GcFunction = OnGc;

		static int OnGc(IntPtr L)
		{
			var vm = GetInstance(L);
			if (vm != null)
			{
				var entry = vm.DataStore.GetEntry(1);

				if (entry != null) // can be null in coroutines - is this correct usage??
					vm.DataStore.Remove(entry.Index);
			}

			return 0;
		}


		public void Glue(Object o, string tableName)
		{
			Glue(o.GetType(), tableName);
		}

		public void Glue(Object o, bool exposeClass)
		{
			Glue(o.GetType(), exposeClass);
		}

		public void Glue(Type t, bool exposeClass)
		{
			string internalType = "";
			string tableName = "";

			tableName = t.FullName.Split('.')[t.FullName.Split('.').Length - 1];

			if (exposeClass)
				Glue(t, tableName);

			Glue(t, t.FullName);
		}

		public void Glue(Type t) { Glue(t, true); }

		public void Glue(Type t, string tableName)
		{
			Glue(t, null, t.FullName, tableName);
		}

		public void Glue(Type t, Object o, string internalType, string tableName)
		{
			if (t.IsEnum)
			{
				GlueEnum(t, internalType, tableName);
				return;
			}

			var staticFunctions = new List<Lua.luaL_Reg>();
			var objectFunctions = new List<Lua.luaL_Reg>();

			var functions = t.GetMethods().Where(m => m.IsStatic);


			var methods = t.GetMethods().Where(m => !m.IsStatic);

			GluedCallback lastCb = null;

			foreach (var mi in functions.OrderBy(f => f.Name))
			{
				var f = (o == null) ? GluedCallback.Wrap(mi) : GluedCallback.Wrap(mi, o);
				var e = new Lua.luaL_Reg {func = f.OnCall, name = mi.Name};

				CallbackReferences.Add(f);
				CallbackReferences2.Add(e);
				if (lastCb == null || lastCb.Method.Name != mi.Name)
				{
					staticFunctions.Add(e);

					lastCb = f;
					if (mi.GetParameters().FirstOrDefault() != null)
					{
						if (mi.GetParameters().FirstOrDefault().ParameterType == t)
						{
							objectFunctions.Add(e);
						}
					}
				}else
				{
					lastCb.Overloads.Add(f);
				}
			}
			
			lastCb = null;
			foreach (var mi in methods.OrderBy(f => f.Name))
			{
				var f = (o == null) ? GluedCallback.Wrap(mi) : GluedCallback.Wrap(mi, o);
				var e = new Lua.luaL_Reg { func = f.OnCall, name = mi.Name };

				CallbackReferences.Add(f);
				CallbackReferences2.Add(e);

				if (lastCb == null || lastCb.Method.Name != mi.Name)
				{
					lastCb = f;
					objectFunctions.Add(e);
				}else
				{

					lastCb.Overloads.Add(f);
				}
			}

			Glue(internalType, tableName, objectFunctions.ToArray(), staticFunctions.ToArray());
		}

		internal void GlueEnum(Type t, string internalType, string tableName)
		{
			var names = Enum.GetNames(t);
			var values = Enum.GetValues(t);

			Lua.lua_newtable(this);
			int i = 0;
			foreach (var name in names)
			{
				Lua.lua_pushstring(this, name);
				
				DataStore.Create(values.GetValue(i), internalType + "." + name);
				Lua.lua_settable(this, -3);
				i++;
			}
			Lua.lua_setglobal(this, tableName);
		}

		private void ExposeEnum2(Type t, string internalType, string tableName)
		{
			var names = Enum.GetNames(t);
			var fields = t.GetFields(BindingFlags.Public | BindingFlags.Static);
			
			Lua.lua_newtable(this);

			int i = 0;
			foreach (FieldInfo fi in fields)
			{
				Lua.lua_pushstring(this, fi.Name);
				//Lua.lua_pushnumber(this, (double)(int)fi.GetRawConstantValue());
				Lua.lua_settable(this, -3);
				i++;
			}
			Lua.lua_setglobal(this, tableName);
		}

		protected string GetErrors(IntPtr L, int status)
		{
			if (status != 0)
			{
				var msg = Lua.lua_tostring(L, -1);
				Lua.lua_pop(L, 1); // remove error message


				return msg;
			}

			return "";
		}

		public void CheckStatus(int status, bool throwException) 
		{
			var msg = GetErrors(this, status);
			if (msg == "")
				return;
			if (throwException )
				throw new Exception("Lua error: " + msg);
			else
				Console.WriteLine("Lua error: " + msg);
		}

		public void Glue(string objectType, string tableName, Lua.luaL_Reg[] methods, Lua.luaL_Reg[] functions)
		{
			Lua.luaL_newmetatable(this, "GLua." + objectType);
			Lua.lua_pushstring(this, "__index");
			Lua.lua_pushvalue(this, -2);  /* pushes the metatable */
			Lua.lua_settable(this, -3);  /* metatable.__index = metatable */


			var gc = new Lua.luaL_Reg();
			gc.name = "__gc";
			gc.func = GcFunction;

			var mt = methods.ToList();
			mt.Add(gc); /* hack in our __gc automagic */
			methods = mt.ToArray();

			// Register the methods for referenced objects
			luaL_module(this, null, methods, 0);

			// Register the 'static' functions 
			luaL_module(this, tableName, functions, 0);

			return;
		}

		public void Glue(string objectType, string tableName, Lua.luaL_Reg[] methods, Lua.lua_CFunction createFunction)
		{
			Lua.luaL_newmetatable(this, "GLua." + objectType);
			Lua.lua_pushstring(this, "__index");
			Lua.lua_pushvalue(this, -2);  /* pushes the metatable */
			Lua.lua_settable(this, -3);  /* metatable.__index = metatable */
			var functions = new List<Lua.luaL_Reg>();
			var ent = new Lua.luaL_Reg();
			ent.name = "new";
			ent.func = createFunction;
			functions.Add(ent);



			var gc = new Lua.luaL_Reg();
			gc.name = "__gc";
			gc.func = GcFunction;

			var mt = methods.ToList();
			mt.Add(gc); /* hack in our __gc automagic */
			methods = mt.ToArray();

			// Register the methods for referenced objects
			luaL_module(this, null, methods, 0);

			// Register the 'static' functions 
			luaL_module(this, tableName, functions.ToArray(), 0);

			return;
		}



		public static void getfield(IntPtr L, string name)
		{
			Lua.lua_pushvalue(L, Lua.LUA_GLOBALSINDEX);
			foreach (var s in name.Split('.'))
			{
				Lua.lua_pushstring(L, name);
				Lua.lua_gettable(L, -2);
				Lua.lua_remove(L, -2);
				if (Lua.lua_isnil(L, -1)) return;
			}
		}

		public static void setfield(IntPtr L, string name)
		{
			Lua.lua_pushvalue(L, Lua.LUA_GLOBALSINDEX);
			var p = name.Split('.');

			for (int i = 0; i < p.Length - 1; i++)
			{
				var s = p[i];
				Lua.lua_pushstring(L, name);
				Lua.lua_gettable(L, -2);

				if (Lua.lua_isnil(L, -1))
				{
					Lua.lua_pop(L, 1);
					Lua.lua_newtable(L);
					Lua.lua_pushstring(L, s);
					Lua.lua_pushvalue(L, -2);
					Lua.lua_settable(L, -4);

				}
				Lua.lua_remove(L, -2);
			}
			Lua.lua_pushstring(L, p[p.Length - 1]);
			Lua.lua_pushvalue(L, -3);
			Lua.lua_settable(L, -3);
			Lua.lua_pop(L, 2);
		}

		public static void luaL_module(IntPtr L, string libname, Lua.luaL_Reg[] regs, int nup)
		{
			if (!string.IsNullOrEmpty(libname))
			{
				getfield(L, libname);  /* check whether lib already exists */
				if (Lua.lua_isnil(L, -1))
				{
					Lua.lua_pop(L, 1); /* get rid of nil */
					Lua.lua_newtable(L); /* create namespace for lib */
					getfield(L, "package.loaded"); /* get package.loaded table or create it */
					if (Lua.lua_isnil(L, -1))
					{
						Lua.lua_pop(L, 1);
						Lua.lua_newtable(L);
						Lua.lua_pushvalue(L, -1);
						setfield(L, "package.loaded");
					}
					else if (!Lua.lua_istable(L, -1))
						Lua.lua_error(L); // @todo fixme , "name conflict for library `%s'", libname);

					Lua.lua_pushstring(L, libname);
					Lua.lua_pushvalue(L, -3);
					Lua.lua_settable(L, -3); /* store namespace in package.loaded table */
					Lua.lua_pop(L, 1); /* get rid of package.loaded table */
					Lua.lua_pushvalue(L, -1);
					setfield(L, libname);  /* store namespace it in globals table */
				}
				Lua.lua_insert(L, -(nup + 1));
			}

			for (int i = 0; i < regs.Length; i++)
			{
				var r = regs[i];
				Lua.lua_pushstring(L, r.name);
				for (int ix = 0; ix < nup; ix++)  /* copy upvalues to the top */
					Lua.lua_pushvalue(L, -(nup + 1));
				Lua.lua_pushcclosure(L, r.func, nup);
				Lua.lua_settable(L, -(nup + 3));
			}
			Lua.lua_pop(L, nup);  /* remove upvalues */
		}

		#region IDisposable
		~GLua()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				// Clean up managed resources

			}

			// Clean up lua & related vars
			if (LuaStack.ToInt64() != 0)
				Lua.lua_close(LuaStack);

			LuaStack = (IntPtr) 0;
		}
		#endregion
	}
}
