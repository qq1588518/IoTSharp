namespace IoTSharp.Numerics.TestProject
{
    [TestClass]
    public sealed class BEEqualOtherTest
    {
        [TestMethod]
        public void BEU16()
        {
           Assert.IsTrue(((BigEndianUInt16)0x1234).Equals(((BigEndianUInt16)0x1234)));
        }
        [TestMethod]
        public void BEU24()
        {
            Assert.IsTrue(((BigEndianUInt24)0x563412).Equals(((BigEndianUInt24)0x563412)));
        }
        [TestMethod]
        public void BEU32()
        {
            Assert.IsTrue(((BigEndianUInt32)0x78563412).Equals(((BigEndianUInt32)0x78563412)));
        }

        [TestMethod]
        public void BEU64()
        {
            Assert.IsTrue(((BigEndianUInt64)0x1234567812345678).Equals(((BigEndianUInt64)0x1234567812345678)));
        }
    }
}
