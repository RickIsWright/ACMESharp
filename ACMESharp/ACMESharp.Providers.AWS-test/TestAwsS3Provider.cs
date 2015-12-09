﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using ACMESharp.ACME;
using ACMESharp.POSH.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACMESharp.Providers.AWS
{
    [TestClass]
    public class TestAwsS3Provider
    {
        private static config.HttpConfig _httpConfig;

        [ClassInitialize]
        public static void Init(TestContext tctx)
        {
            using (var fs = new FileStream("config\\httpConfig.json", FileMode.Open))
            {
                _httpConfig = JsonHelper.Load<config.HttpConfig>(fs);
            }
        }

        public static AwsS3ChallengeHandlerProvider GetProvider()
        {
            return new AwsS3ChallengeHandlerProvider();
        }

        public static AwsS3ChallengeHandler GetHandler(Challenge c)
        {
            return (AwsS3ChallengeHandler)GetProvider().GetHandler(c, _httpConfig);
        }

        [TestMethod]
        public void TestParameterDescriptions()
        {
            var p = GetProvider();
            var dp = p.DescribeParameters();

            Assert.IsNotNull(dp);
            Assert.IsTrue(dp.Count() > 0);
        }

        [TestMethod]
        public void TestSupportedChallenges()
        {
            var p = GetProvider();

            Assert.IsTrue(p.IsSupported(new HttpChallenge()));
            Assert.IsFalse(p.IsSupported(new DnsChallenge()));
            Assert.IsFalse(p.IsSupported(new TlsSniChallenge()));
            Assert.IsFalse(p.IsSupported(new FakeChallenge()));
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void TestRequiredParams()
        {
            var p = GetProvider();
            var c = new HttpChallenge();
            var h = p.GetHandler(c, new Dictionary<string, object>());
        }

        [TestMethod]
        public void TestHandlerLifetime()
        {
            var p = GetProvider();
            var c = new HttpChallenge();
            var h = p.GetHandler(c, _httpConfig);

            Assert.IsNotNull(h);
            Assert.IsFalse(h.IsDisposed);
            h.Dispose();
            Assert.IsTrue(h.IsDisposed);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestHandlerDisposedAccess()
        {
            var p = GetProvider();
            var c = new HttpChallenge();
            var h = p.GetHandler(c, _httpConfig);

            h.Dispose();
            h.Handle(null);
        }

        [TestMethod]
        public void TestHandlerDefineAndCleanUpResourceRecord()
        {
            var r = new Random();
            var bn = new byte[10];
            var bv = new byte[10];
            r.NextBytes(bn);
            r.NextBytes(bv);
            var rn = BitConverter.ToString(bn);
            var rv = BitConverter.ToString(bv);

            var c = new HttpChallenge
            {
                TypeKind = ChallengeTypeKind.HTTP,
                Type = AcmeProtocol.CHALLENGE_TYPE_HTTP,
                Token = "FOOBAR",
                FileUrl = $"http://foobar.acmetesting.zyborg.io/utest/{rn}",
                FilePath = $"/utest/{rn}",
                FileContent = rv,
            };

            var awsParams = new AwsCommonParams();
            awsParams.InitParams(_httpConfig);

            var p = GetProvider();
            using (var h = p.GetHandler(c, _httpConfig))
            {
                // Assert test file does not exist
                var s3Obj = AwsS3ChallengeHandler.GetFile(awsParams,
                        _httpConfig.BucketName, c.FilePath);
                
                // Assert test record does *not* exist
                Assert.IsNull(s3Obj);

                // Create the record...
                h.Handle(c);

                // ...and assert it does exist
                s3Obj = AwsS3ChallengeHandler.GetFile(awsParams,
                        _httpConfig.BucketName, c.FilePath);

                Assert.IsNotNull(s3Obj);
                using (var sr = new StreamReader(s3Obj.ResponseStream))
                {
                    var v = sr.ReadToEnd();
                    Assert.AreEqual(c.FileContent, v);
                }

                // Clean up the record...
                h.CleanUp(c);

                // ...and assert it does not exist once more
                s3Obj = AwsS3ChallengeHandler.GetFile(awsParams,
                        _httpConfig.BucketName, c.FilePath);

                Assert.IsNull(s3Obj);
            }
        }

        class FakeChallenge : Challenge
        { }
    }
}
