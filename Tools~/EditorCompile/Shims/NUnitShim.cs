// 只服务于离线编译检查的 NUnit 门面（shim）。
//
// 为什么需要它：Unity 官方的 nunit.framework.dll 由 com.unity.test-framework 包提供，
// 而包 DLL 只在创建工程时才会被解包到 Library/PackageCache，Unity 安装目录里找不到。
// 于是「不开编辑器编译一遍 Tests/」会缺类型。
//
// 这里只补齐测试代码用到的 API 形状，方法体一律不实现（要么空实现，要么直接抛异常）——
// 它的唯一职责是让 C# 编译器把 Tests/**/*.cs 完整解析一遍，
// 提前暴露拼写错误、签名不匹配、缺 using 之类的问题。
//
// 真正的断言行为由 Unity Test Runner（真 nunit）提供，不经过这个文件。

using System;
using System.Collections;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TestAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SetUpAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TearDownAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class OneTimeSetUpAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class OneTimeTearDownAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseAttribute : Attribute
    {
        public TestCaseAttribute(params object[] arguments)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseSourceAttribute : Attribute
    {
        public TestCaseSourceAttribute(string sourceName)
        {
        }

        public TestCaseSourceAttribute(Type sourceType, string sourceName)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ValuesAttribute : Attribute
    {
        public ValuesAttribute(params object[] values)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class CategoryAttribute : Attribute
    {
        public CategoryAttribute(string name)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class IgnoreAttribute : Attribute
    {
        public IgnoreAttribute(string reason)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ExplicitAttribute : Attribute
    {
        public ExplicitAttribute(string reason)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DescriptionAttribute : Attribute
    {
        public DescriptionAttribute(string description)
        {
        }
    }

    public delegate void TestDelegate();

    public class AssertionException : Exception
    {
        public AssertionException(string message)
            : base(message)
        {
        }
    }

    public class IgnoreException : Exception
    {
        public IgnoreException(string message)
            : base(message)
        {
        }
    }

    public static class Assert
    {
        public static void AreEqual(object expected, object actual)
        {
        }

        public static void AreEqual(object expected, object actual, string message, params object[] args)
        {
        }

        public static void AreEqual(double expected, double actual, double delta)
        {
        }

        public static void AreEqual(double expected, double actual, double delta, string message, params object[] args)
        {
        }

        public static void AreNotEqual(object expected, object actual)
        {
        }

        public static void AreNotEqual(object expected, object actual, string message, params object[] args)
        {
        }

        public static void AreSame(object expected, object actual)
        {
        }

        public static void AreSame(object expected, object actual, string message, params object[] args)
        {
        }

        public static void AreNotSame(object expected, object actual)
        {
        }

        public static void AreNotSame(object expected, object actual, string message, params object[] args)
        {
        }

        public static void IsTrue(bool condition)
        {
        }

        public static void IsTrue(bool condition, string message, params object[] args)
        {
        }

        public static void IsFalse(bool condition)
        {
        }

        public static void IsFalse(bool condition, string message, params object[] args)
        {
        }

        public static void IsNull(object anObject)
        {
        }

        public static void IsNull(object anObject, string message, params object[] args)
        {
        }

        public static void IsNotNull(object anObject)
        {
        }

        public static void IsNotNull(object anObject, string message, params object[] args)
        {
        }

        public static void IsEmpty(string aString)
        {
        }

        public static void IsEmpty(string aString, string message, params object[] args)
        {
        }

        public static void IsEmpty(IEnumerable collection)
        {
        }

        public static void IsNotEmpty(string aString)
        {
        }

        public static void IsNotEmpty(IEnumerable collection)
        {
        }

        public static void Greater(IComparable arg1, IComparable arg2)
        {
        }

        public static void Greater(IComparable arg1, IComparable arg2, string message, params object[] args)
        {
        }

        public static void GreaterOrEqual(IComparable arg1, IComparable arg2)
        {
        }

        public static void Less(IComparable arg1, IComparable arg2)
        {
        }

        public static void Less(IComparable arg1, IComparable arg2, string message, params object[] args)
        {
        }

        public static void LessOrEqual(IComparable arg1, IComparable arg2)
        {
        }

        public static void Contains(object expected, ICollection actual)
        {
        }

        public static void Fail()
        {
        }

        public static void Fail(string message, params object[] args)
        {
        }

        public static void Pass()
        {
        }

        public static void Pass(string message, params object[] args)
        {
        }

        public static void Ignore(string message, params object[] args)
        {
        }

        public static T Throws<T>(TestDelegate code)
            where T : Exception
        {
            return Throws<T>(code, null);
        }

        public static T Throws<T>(TestDelegate code, string message, params object[] args)
            where T : Exception
        {
            if (code == null)
            {
                throw new ArgumentNullException(nameof(code));
            }

            code();
            throw new AssertionException("期待抛出 " + typeof(T).Name + "：" + message);
        }

        public static Exception Throws(Type expected, TestDelegate code)
        {
            return Throws<Exception>(code);
        }

        public static void DoesNotThrow(TestDelegate code)
        {
        }

        public static void DoesNotThrow(TestDelegate code, string message, params object[] args)
        {
        }
    }

    public static class StringAssert
    {
        public static void Contains(string expected, string actual)
        {
        }

        public static void Contains(string expected, string actual, string message, params object[] args)
        {
        }

        public static void DoesNotContain(string expected, string actual)
        {
        }

        public static void StartsWith(string expected, string actual)
        {
        }

        public static void EndsWith(string expected, string actual)
        {
        }

        public static void AreEqualIgnoringCase(string expected, string actual)
        {
        }

        public static void IsMatch(string pattern, string actual)
        {
        }
    }

    public static class CollectionAssert
    {
        public static void AreEqual(IEnumerable expected, IEnumerable actual)
        {
        }

        public static void AreEqual(IEnumerable expected, IEnumerable actual, string message, params object[] args)
        {
        }

        public static void AreEquivalent(IEnumerable expected, IEnumerable actual)
        {
        }

        public static void Contains(IEnumerable collection, object item)
        {
        }

        public static void IsEmpty(IEnumerable collection)
        {
        }

        public static void IsNotEmpty(IEnumerable collection)
        {
        }
    }
}
