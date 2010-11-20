using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Geckosoft;
using Geckosoft.Scripting;

namespace Test1
{
	class Program
	{
		static void Main(string[] args)
		{
			var glua = GLua.New();
			glua.Glue(typeof(MessageBox));
			glua.Glue(typeof(MessageBoxButtons));
			glua.Glue(typeof(DialogResult));

			glua.Eval("res = MessageBox.Show(\"message\", \"titel\", MessageBoxButtons.OKCancel)");
			glua.Eval("if (res == DialogResult.OK) then MessageBox.Show(\"got it\"); end");
			Console.ReadLine();
		}
	}

	class TestObject
	{
		public static void WriteLine(string msg)
		{
			Console.WriteLine(msg);
		}

		public static TestObject Create()
		{
			return new TestObject();
		}

		public static void Hello(TestObject obj)
		{
			Console.WriteLine("World");
		}
		/*
		public void Hello()
		{
			Console.WriteLine("World");
		}*/
	}
}
