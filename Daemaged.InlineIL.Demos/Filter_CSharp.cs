using System;
class Foo
{
    static void ThrowMe()
    {
        throw new ArgumentException();
    }

    static void Main()
    {
        string x;
        object ex = null;
#if IL
        // declare a new local. 
        .locals init (int32 ghi)

        .try
        {
#endif
            x = "a";
            ThrowMe();

#if IL
            leave.s IL_ExitTryCatch
        } // end try block
        filter
        {
            // Exception object is on the stack.
            stloc ex
#endif
            Console.WriteLine("Inside Filter. Object=" + ex);
            x += "b";

#if IL
            ldc.i4.1 // true - execute handler
            endfilter
        } // end filter
        { // begin handler
#endif
            Console.WriteLine("Yow! In handler now!");
            x += "c";
#if IL
            leave.s IL_ExitTryCatch
        }  // end handler
        IL_ExitTryCatch:  nop
#endif

        Console.WriteLine("Back in C#");
        Console.WriteLine(x);
    } // end Main
} // end Class Foo