﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Internal;
using Microsoft.Identity.Client.OAuth2;
using Microsoft.Identity.Client.UI;
using Microsoft.Identity.Test.Common.Core.Helpers;
using Microsoft.Identity.Test.Common.Core.Mocks;
using Microsoft.Identity.Test.Common.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Security.Credentials;

namespace Microsoft.Identity.Test.Unit
{
    [TestClass]
    public class OAuthClientTests : TestBase
    {
        [TestMethod]
        public async Task OAuthClient_FailsWithServiceExceptionWhenItCannotParseJsonResponse_Async()
        {
            await ValidateOathClientAsync(
                MockHelpers.CreateTooManyRequestsNonJsonResponse(),
                exception =>
                {
                    MsalServiceException serverEx = exception as MsalServiceException;
                    Assert.IsNotNull(serverEx);
                    Assert.AreEqual(429, serverEx.StatusCode);
                    Assert.AreEqual(MockHelpers.TooManyRequestsContent, serverEx.ResponseBody);
                    Assert.AreEqual(MockHelpers.TestRetryAfterDuration, serverEx.Headers.RetryAfter.Delta);
                    Assert.AreEqual(MsalError.NonParsableOAuthError, serverEx.ErrorCode);
                }).ConfigureAwait(false);
        }

        [TestMethod]
        public async Task OAuthClient_FailsWithServiceExceptionWhenItCanParseJsonResponse_Async()
        {
            await ValidateOathClientAsync(
                MockHelpers.CreateTooManyRequestsJsonResponse(),
                exception =>
                {
                    MsalServiceException serverEx = exception as MsalServiceException;
                    Assert.IsNotNull(serverEx);
                    Assert.AreEqual(429, serverEx.StatusCode);
                    Assert.AreEqual(MockHelpers.TestRetryAfterDuration, serverEx.Headers.RetryAfter.Delta);
                    Assert.AreEqual("Server overload", serverEx.ErrorCode);
                }).ConfigureAwait(false);
        }

        [TestMethod]
        public async Task OAuthClient_FailsWithServiceExceptionWhenEntireResponseIsNull_Async()
        {
            await ValidateOathClientAsync(
                null,
                exception =>
                {
                    var innerException = exception as InvalidOperationException;
                    Assert.IsNotNull(innerException);
                }).ConfigureAwait(false);
        }

        [TestMethod]
        public async Task OAuthClient_FailsWithServiceExceptionWhenResponseIsEmpty_Async()
        {
            await ValidateOathClientAsync(
                MockHelpers.CreateEmptyResponseMessage(),
                exception =>
                {
                    var serverEx = exception as MsalServiceException;
                    Assert.IsNotNull(serverEx);
                    Assert.AreEqual((int)HttpStatusCode.BadRequest, serverEx.StatusCode);
                    Assert.IsNotNull(serverEx.ResponseBody);
                    Assert.AreEqual(MsalError.HttpStatusCodeNotOk, serverEx.ErrorCode);
                }).ConfigureAwait(false);
        }

        [TestMethod]
        public async Task OAuthClient_FailsWithServiceExceptionWhenResponseIsNull_Async()
        {
            await ValidateOathClientAsync(
                MockHelpers.CreateNullResponseMessage(),
                exception =>
                {
                    var serverEx = exception as MsalServiceException;
                    Assert.IsNotNull(serverEx);
                    Assert.AreEqual((int)HttpStatusCode.BadRequest, serverEx.StatusCode);
                    Assert.IsNull(serverEx.ResponseBody);
                    Assert.AreEqual(MsalError.HttpStatusCodeNotOk, serverEx.ErrorCode);
                }).ConfigureAwait(false);
        }

        [TestMethod]
        public async Task OAuthClient_FailsWithServiceExceptionWhenResponseDoesNotContainAnErrorField_Async()
        {
            await ValidateOathClientAsync(
                MockHelpers.CreateNoErrorFieldResponseMessage(),
                exception =>
                {
                    var serverEx = exception as MsalServiceException;
                    Assert.IsNotNull(serverEx);
                    Assert.AreEqual((int)HttpStatusCode.BadRequest, serverEx.StatusCode);
                    Assert.IsNotNull(serverEx.ResponseBody);
                    Assert.AreEqual(MsalError.HttpStatusCodeNotOk, serverEx.ErrorCode);
                }).ConfigureAwait(false);
        }

        [TestMethod]
        public async Task OAuthClient_FailsWithServiceExceptionWhenResponseIsHttpNotFound_Async()
        {
            await ValidateOathClientAsync(
                MockHelpers.CreateHttpStatusNotFoundResponseMessage(),
                exception =>
                {
                    var serverEx = exception as MsalServiceException;
                    Assert.IsNotNull(serverEx);
                    Assert.AreEqual((int)HttpStatusCode.NotFound, serverEx.StatusCode);
                    Assert.IsNotNull(serverEx.ResponseBody);
                    Assert.AreEqual(MsalError.HttpStatusNotFound, serverEx.ErrorCode);
                }).ConfigureAwait(false);
        }

#if DESKTOP
        [TestMethod]
        public void PKeyAuthSuccsesResponseTest()
        {
            using (var harness = CreateTestHarness())
            {
                harness.HttpManager.AddInstanceDiscoveryMockHandler();

                PublicClientApplication app = PublicClientApplicationBuilder.Create(TestConstants.ClientId)
                                                                            .WithAuthority(new Uri(ClientApplicationBase.DefaultAuthority), true)
                                                                            .WithHttpManager(harness.HttpManager)
                                                                            .WithTelemetry(new TraceTelemetryConfig())
                                                                            .BuildConcrete();

                MsalMockHelpers.ConfigureMockWebUI(
                    app.ServiceBundle.PlatformProxy,
                    AuthorizationResult.FromUri(app.AppConfig.RedirectUri + "?code=some-code"));

                //Initiates PKeyAuth challenge which will trigger an additional request sent to AAD to satisfy the PKeyAuth challenge
                harness.HttpManager.AddMockHandler(
                    new MockHttpMessageHandler
                    {
                        ExpectedMethod = HttpMethod.Post,
                        ResponseMessage = MockHelpers.CreatePKeyAuthChallengeResponse()
                    });
                harness.HttpManager.AddMockHandler(CreateTokenResponseHttpHandlerWithPKeyAuthValidation());

                AuthenticationResult result = app
                    .AcquireTokenInteractive(TestConstants.s_scope)
                    .ExecuteAsync(CancellationToken.None)
                    .Result;
            }
        }

        private static MockHttpMessageHandler CreateTokenResponseHttpHandlerWithPKeyAuthValidation()
        {
            return new MockHttpMessageHandler()
            {
                ExpectedMethod = HttpMethod.Post,
                ResponseMessage = MockHelpers.CreateSuccessTokenResponseMessage(),
                AdditionalRequestValidation = request =>
                {
                    var authHeader = request.Headers.Authorization?.ToString();

                    // Check value of pkeyAuth header.
                    Assert.AreEqual(authHeader, TestConstants.PKeyAuthResponse);
                }
            };
        }
#endif

        private async Task ValidateOathClientAsync(
            HttpResponseMessage httpResponseMessage, 
            Action<Exception> validationHandler)
        {
            using (MockHttpAndServiceBundle harness = CreateTestHarness())
            {
                var requestUri = new Uri("http://some.url.com");

                harness.HttpManager.AddMockHandler(
                  new MockHttpMessageHandler
                  {
                      ExpectedUrl = requestUri.AbsoluteUri,
                      ExpectedMethod = HttpMethod.Post,
                      ResponseMessage = httpResponseMessage
                  });

                OAuth2Client client = new OAuth2Client(
                    harness.ServiceBundle.DefaultLogger,
                    harness.HttpManager,
                    harness.ServiceBundle.MatsTelemetryManager);

                Exception ex = await AssertException.TaskThrowsAsync<Exception>(
                    () => client.ExecuteRequestAsync<OAuth2ResponseBase>(
                        requestUri,
                        HttpMethod.Post,
                        new RequestContext(harness.ServiceBundle, Guid.NewGuid())), 
                    allowDerived: true)
                    .ConfigureAwait(false);

                validationHandler(ex);
            }
        }

    }
}
