using System;
using System.Collections.Generic;
using System.Text;

namespace DDR4_TestingApp
{
    public struct MemoryBlock
    {

        //Memory address of block
        public UInt32 address;

        //Length of bytes in this block
        public UInt32 size;

        //Data section
        public Byte[] data;

        // ===================== Helper functions =========================

        public UInt32 getStartAddress()
        {
            return (UInt32)address;
        }
        public UInt32 getEndAddress()
        {
            return (UInt32)address + size;
        }

    }
}
