namespace IoTSharp.Numerics.TestProject
{
    [TestClass]
    public sealed class BEEqualBytesTest
    {
        [TestMethod]
        public void BEU16()
        {
            Assert.AreEqual(Convert.ToHexString(((byte[])(BigEndianUInt16)0x1234)), Convert.ToHexString(new byte[] { 0x12, 0x34 }));
        }
        [TestMethod]
        public void BEU24()
        {
            Assert.AreEqual(Convert.ToHexString(((byte[])(BigEndianUInt24)0x123456)), Convert.ToHexString(new byte[] { 0x12, 0x34,0x56 }));
        }
        [TestMethod]
        public void BEU32()
        {
            Assert.AreEqual(Convert.ToHexString(((byte[])(BigEndianUInt32)0x12345678)), Convert.ToHexString(new byte[] { 0x12, 0x34, 0x56 ,0x78}));
        }

        [TestMethod]
        public void BEU64()
        {
            Assert.AreEqual(Convert.ToHexString(((byte[])(BigEndianUInt64)0x1234567812345678)), Convert.ToHexString(new byte[] { 0x12, 0x34, 0x56, 0x78 , 0x12, 0x34, 0x56, 0x78 }));
        }
    }
}
