namespace IoTSharp.Numerics.TestProject
{
    [TestClass]
    public sealed class BEHexAreEqualTest
    {
        [TestMethod]
        public void BEU16()
        {
            Assert.AreEqual(((BigEndianUInt16)0x1234).ToString(),"1234");
        }
        [TestMethod]
        public void BEU24()
        {
            Assert.AreEqual(((BigEndianUInt24)0x123456).ToString(), "123456");
        }
        [TestMethod]
        public void BEU32()
        {
            Assert.AreEqual(((BigEndianUInt32)0x12345678).ToString(), "12345678");
        }

        [TestMethod]
        public void BEU64()
        {
            Assert.AreEqual(((BigEndianUInt64)0x1234567812345678).ToString(), "1234567812345678");
        }
    }
}
