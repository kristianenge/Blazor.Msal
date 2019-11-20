﻿using Des.Blazor.Msal.Utils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Des.Blazor.Authorization.Msal
{
    public class MsalAuthenticationStateProvider : AuthenticationStateProvider
    {
        private IJSRuntime _js;
        private HttpClient _http;
        private NavigationManager _navigation;
        private ConditionalInvoker _conditionalInvoker;
        private IConfigProvider<IMsalConfig> _configProvider;
        private string _loginMode;

        public bool IsInitialized { get; private set; }

        public MsalAuthenticationStateProvider(IJSRuntime js, HttpClient http, NavigationManager navigation, IConfigProvider<IMsalConfig> configProvider)
        {
            _js = js;
            _http = http;
            _navigation = navigation;
            _conditionalInvoker = new ConditionalInvoker(
                () => this.AuthenticationChanged());
            _configProvider = configProvider;
        }

        public async override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            await this.EnsureInitializedAsync();

            var account = await _js.InvokeAsync<MsalAccount>("azuread.getAccount");

            if (account == null)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            Console.WriteLine(account);

            return new AuthenticationState(account.GetPrincipal());
        }

        public async Task InitializeAsync(IMsalConfig config)
        {
            var msalConfig = new
            {
                auth = new
                {
                    clientId = config.ClientId,
                    authority = config.Authority,
                    // needed to avoid the issue with iFrame src
                    redirectUri = _navigation.BaseUri,
                    navigateToLoginRequestUrl = false
                },
                cache = new
                {
                    cacheLocation = "localStorage",
                    storeAuthStateInCookie = true
                }
            };

            _loginMode = Enum.GetName(typeof(LoginModes), config.LoginMode);

            Console.WriteLine("azuread.initializing");

            await _js.InvokeVoidAsync("azuread.initialize", 
                new object[] { msalConfig, DotNetObjectReference.Create(this) });

            Console.WriteLine("azuread.initialized");

            this.IsInitialized = true;
        }

        public async Task SignInAsync(params string[] scopes)
        {
            await this.EnsureInitializedAsync();

            await using (await _conditionalInvoker.InvokeIfChanged(
                async () => (await this.GetAuthenticationStateAsync()).User.Identity.Name))
            {
                await _js.InvokeVoidAsync("azuread.signIn" + _loginMode, new object[] { scopes });
            }
        }

        public async Task<MsalToken> GetAccessTokenAsync(params string[] scopes)
        {
            await this.EnsureInitializedAsync();

            await using (await _conditionalInvoker.InvokeIfChanged(
                async () => (await this.GetAuthenticationStateAsync()).User.Identity.Name))
            {
                var token = await _js.InvokeAsync<MsalToken>("azuread.acquireToken" + _loginMode,
                    new object[] { scopes });

                Console.WriteLine($"AccessToken: {token?.AccessToken}");

                return token;
            }
        }

        public async Task SignOutAsync()
        {
            await this.EnsureInitializedAsync();

            await _js.InvokeVoidAsync("azuread.signOut");

            await AuthenticationChanged();
        }

        private async Task EnsureInitializedAsync()
        {
            if (this.IsInitialized)
            {
                return;
            }

            var config = await _configProvider.GetConfigurationAsync();

            await this.InitializeAsync(config);
        }

        public async Task AuthenticationChanged()
        {
            Console.WriteLine("AuthenticationChanged called");

            var state = await GetAuthenticationStateAsync();

            Console.WriteLine($"AuthenticationChanged called! State is {state.User?.Identity.Name}");
            this.NotifyAuthenticationStateChanged(Task.FromResult(state));
        }

        [JSInvokable]
        public void RedirectToSourceUrl(string url)
        {
            _navigation.NavigateTo(url);
        }
    }
}