using System;

namespace RobloxKeeper.Tests
{
    // P4: a pinned client must keep the same block of cores for its whole life.
    //
    // The old code derived the block from the client's index in a list sorted by
    // start time, so closing an early client shifted every later client's index.
    // Nothing re-applied the mask afterwards, so the promise that "4 cores" on
    // two clients means two non-overlapping sets quietly stopped holding.
    static class CoreBlocksTests
    {
        public static void TestGivesEachClientADifferentBlock()
        {
            CoreBlocks blocks = new CoreBlocks();
            int a = blocks.BlockFor(100);
            int b = blocks.BlockFor(200);
            int c = blocks.BlockFor(300);

            Assert.NotEqual(a, b, "second client's block");
            Assert.NotEqual(b, c, "third client's block");
            Assert.NotEqual(a, c, "third vs first client's block");
        }

        public static void TestKeepsTheSameBlockOnEveryLookup()
        {
            CoreBlocks blocks = new CoreBlocks();
            int first = blocks.BlockFor(100);

            Assert.Equal(first, blocks.BlockFor(100), "same pid asked twice");
            Assert.Equal(first, blocks.BlockFor(100), "same pid asked a third time");
        }

        // The regression this class exists for.
        public static void TestSurvivingClientKeepsItsBlockWhenAnEarlierOneCloses()
        {
            CoreBlocks blocks = new CoreBlocks();
            blocks.BlockFor(100);
            int second = blocks.BlockFor(200);

            blocks.Release(100);

            Assert.Equal(second, blocks.BlockFor(200), "survivor's block after the earlier client closed");
        }

        public static void TestReusesABlockOnlyAfterItIsReleased()
        {
            CoreBlocks blocks = new CoreBlocks();
            int a = blocks.BlockFor(100);
            blocks.BlockFor(200);

            blocks.Release(100);
            int reused = blocks.BlockFor(300);

            Assert.Equal(a, reused, "freed block handed to the next client");
        }

        public static void TestNewClientNeverCollidesWithLiveOnes()
        {
            CoreBlocks blocks = new CoreBlocks();
            int a = blocks.BlockFor(100);
            int b = blocks.BlockFor(200);
            blocks.Release(100);

            int c = blocks.BlockFor(300);   // takes the freed block
            int d = blocks.BlockFor(400);   // must not take the live one

            Assert.Equal(a, c, "third client takes the freed block");
            Assert.NotEqual(b, d, "fourth client must not collide with the live client");
            Assert.NotEqual(c, d, "fourth client must not collide with the third");
        }

        // A released pid that comes back is a recycled PID on Windows, so it is
        // simply a new client and must be allocated afresh.
        public static void TestReleasedPidIsTreatedAsNewIfItReturns()
        {
            CoreBlocks blocks = new CoreBlocks();
            blocks.BlockFor(100);
            int other = blocks.BlockFor(200);
            blocks.Release(100);
            blocks.Release(200);

            int back = blocks.BlockFor(200);
            Assert.Equal(0, back, "recycled pid allocated from scratch");
            Assert.NotEqual(-1, other, "sanity: original allocation happened");
        }
    }
}
