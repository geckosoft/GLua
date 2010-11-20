using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Tao.Lua;

namespace Geckosoft.Scripting.LuaCore
{
	public class GluedDataStore
	{
		public ulong TypeId = 0;

		internal GLua VM = null;

		internal static ulong CurrentId = 0;
		internal static ulong GenerateId() { return ++CurrentId; }

		internal Dictionary<ulong, GluedData> Entries = new Dictionary<ulong, GluedData>();

		internal GluedDataStore(GLua vm)
        {
            VM = vm;
        }


		internal void Remove(ulong entryId)
		{
			lock (VM)
				if (Entries.ContainsKey(entryId))
					Entries.Remove(entryId); /* free up */
		}

		internal void Clear()
		{
			lock (VM)
				Entries.Clear();
		}

		/// <summary>
		/// Created a GluedData instance but does NOT create (insert data into lua) just yet!
		/// </summary>
		internal GluedData CreateVar(Object obj, string objectType)
		{
			lock (VM)
			{
				string ot = objectType;
				var entry = GlueData(obj);

				entry.ObjectType = ot;

				return entry;
			}
		}

		/// <summary>
		/// Created a GluedData instance but does NOT create (insert data into lua) just yet!
		/// </summary>
		internal GluedData CreateVar(Object obj)
		{
			lock (VM)
			{
				var entry = GlueData(obj);

				return entry;
			}
		}

		internal GluedData Create(Object obj, string objectType)
		{
			lock (VM)
			{
				string ot = "GLua." + objectType;
				var entry = GlueData(obj);

				entry.ObjectType = ot;

				entry.Inject(VM);

				return entry;
			}
		}


		internal GluedData Create(Object obj)
		{
			lock (VM)
			{
				GluedData entry = GlueData(obj);

				entry.Inject(VM);

				return entry;
			}
		}

		internal GluedData GlueData(Object obj)
		{
			lock (VM)
			{
				ulong index = GenerateId();
				var de = new GluedData(obj, index);

				Entries.Add(index, de);

				return de;
			}
		}


		internal object GetObject(int i)
		{
			var obj = GetEntry(i);

			if (obj == null)
				return null;

			return obj.Object;
		}

		internal object GetObject(IntPtr entry)
		{
			var obj = GetEntry(entry);

			if (obj == null)
				return null;

			return obj.Object;
		}

		internal GluedData GetEntry(IntPtr entry)
		{
			try
			{
				var data = new byte[8];

				Marshal.Copy(entry, data, 0, 8);

				ulong index = BitConverter.ToUInt64(data, 0);

				if (!Entries.ContainsKey(index))
					return null;

				return Entries[index];
			}
			catch (Exception)
			{
				return null;
			}
		}

		internal GluedData GetEntry(int i, string objectType)
		{
			if (i < 0)
				throw new Exception("Invalid entry id. Must be > 0");

			string ot = "GLua." + objectType;

			IntPtr entry = Lua.luaL_checkudata(VM, i, ot);
			if (entry == IntPtr.Zero)
				return null; /* not found */

			return GetEntry(entry);
		}

		internal GluedData GetEntry(int i)
		{
			if (i < 0)
				throw new Exception("Invalid entry id. Must be > 0");

			IntPtr entry = Lua.lua_touserdata(VM, i);
			try
			{
				var data = new byte[8];

				Marshal.Copy(entry, data, 0, 8);

				ulong index = BitConverter.ToUInt64(data, 0);

				if (!Entries.ContainsKey(index))
					return null;
				else
				{
					return Entries[index];
				}
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
