using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace DDR4_TestingApp
{
    public class Test
    {
        //LET
        public float let;

        //Data capture from this test
        public Byte[]? data;

        //Test results
        public UInt64 num_correct;
        public UInt64 num_failed;
        public float time_spent_ms;

    }
}
