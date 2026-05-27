using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Text;

namespace DDR4_TestingApp
{
    public struct Capture
    {
        //Name of capture
        public String name = "Unnamed Capture";

        //Unix time of capture
        public UInt64 time;

        //Collection of tests
        public List<Test> tests;

        public Capture()
        {
            tests = new List<Test>();
        }
    }
}
