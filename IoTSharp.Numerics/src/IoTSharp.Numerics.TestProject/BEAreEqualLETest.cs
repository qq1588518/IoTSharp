namespace IoTSharp.Numerics.TestProject
{
    [TestClass]
    public sealed class BEAreEqualLETest
    {
        [TestMethod]
        public void BEU16()
        {
           Assert.AreEqual((ushort)(BigEndianUInt16)( new byte[]{ 0x12,0x34}) , 0x1234);
        }
        [TestMethod]
        public void BEU24()
        {
            Assert.AreEqual((UInt32)(BigEndianUInt24)(new byte[] { 0x12, 0x34,0x56 }),(UInt32) 0x123456);
        }
        [TestMethod]
        public void BEU32()
        {
            Assert.AreEqual((UInt32)(BigEndianUInt32)(new byte[] { 0x12, 0x34,0x56,0x78 }), (UInt32)0x12345678);
        }

        [TestMethod]
        public void BEU64()
        {
            Assert.AreEqual((UInt64)0x1234567812345678, (UInt64)(BigEndianUInt64)(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x12, 0x34, 0x56, 0x78 }));
        }
    }
}
