using System;

namespace Geckosoft
{
    public interface IGLuaObject
    {
        string ObjectType { get; }
    }

	public class GLuaObject : IGLuaObject
	{
		public virtual string ObjectType
		{
			get
			{
				if (GetType().FullName == null)
					return "GLuaObject";

				return GetType().FullName;
			}
		}
	}
}
