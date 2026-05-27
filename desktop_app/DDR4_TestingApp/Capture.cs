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

        //Collection of memory blocks that belong to this capture
        [Browsable(false)]
        public List<MemoryBlock> blocks;

        public Capture()
        {
            blocks = new List<MemoryBlock>();
        }

        // ============== Helper functions ==============

        public UInt32 getStartAddress()
        {

            uint addr = blocks[0].getStartAddress();

            //Iterate through blocks and find the lowest starting address
            foreach (MemoryBlock block in blocks)
            {
                if(block.getStartAddress() <= addr) addr = block.getStartAddress();
            }

            return addr;
        }
        public UInt32 getEndAddress()
        {
            uint addr = blocks[0].getEndAddress();

            //Iterate through blocks and find the highest ending address
            foreach (MemoryBlock block in blocks)
            {
                if (block.getEndAddress() >= addr) addr = block.getEndAddress();
            }

            return addr;
        }

        public UInt32 getSize()
        {
            uint size = 0;

            //Iterate through blocks and sum the bytes
            foreach (MemoryBlock block in blocks)
            {
                size += block.size;
            }

            return size;
        }


    }

    public class CaptureInfo
    {
        // Name of capture
        public String Name;

        // EST of time stamp in 24/hr time (MM/DD/YY HH:MM:SS)
        public String Time;

        //Size of capture (in readable form)
        public String Size;

        // Start memory address (in HEX)
        public String StartAddress;

        // End memory address (in HEX)
        public String EndAddress;

    }
}
