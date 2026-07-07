namespace IoTSharp.Numerics.TestProject
{
    [TestClass]
    public sealed class BEHashCodeTest
    {
        [TestMethod]
        public void BEU16()
        {
            Assert.AreEqual(((BigEndianUInt16)(0x1234)).GetHashCode(), ((BigEndianUInt16)(0x1234)).GetHashCode());
        }
        [TestMethod]
        public void BEU24()
        {
            Assert.AreEqual(((BigEndianUInt24)(0x123456)).GetHashCode(), ((BigEndianUInt24)(0x123456)).GetHashCode());
        }
        [TestMethod]
        public void BEU32()
        {
            Assert.AreEqual(((BigEndianUInt32)(0x12345678)).GetHashCode(), ((BigEndianUInt32)(0x12345678)).GetHashCode());
        }

        [TestMethod]
        public void BEU64()
        {
            Assert.AreEqual(((BigEndianUInt64)(0x1234567812345678)).GetHashCode(), ((BigEndianUInt64)(0x1234567812345678)).GetHashCode());
        }
    }
}
