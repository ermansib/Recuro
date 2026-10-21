<#-- Recuro layout for every Keycloak sign-in page: the portal's split screen (workspace branding on the
     left, one card on the right). js/recuro.js fills in the workspace's name and colours that the portal
     passed along, so the page reads as the next step of the portal's own sign-in card.
     showHeader=false lets a page (login.ftl) lay out its own heading, steps and messages. -->
<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false showHeader=true>
<!DOCTYPE html>
<html lang="${lang}" data-theme="light">
<head>
    <meta charset="utf-8">
    <#if properties.meta?has_content>
        <#list properties.meta?split(' ') as meta>
            <meta name="${meta?split('==')[0]}" content="${meta?split('==')[1]}"/>
        </#list>
    </#if>
    <meta name="robots" content="noindex, nofollow">
    <title>${msg("loginTitle")}</title>
    <link rel="icon" type="image/svg+xml" href="${url.resourcesPath}/img/favicon.svg" />
    <#if properties.styles?has_content>
        <#list properties.styles?split(' ') as style>
            <link href="${url.resourcesPath}/${style}" rel="stylesheet" />
        </#list>
    </#if>
    <#-- Runs before the body renders so the workspace's colours and light/dark mode apply without a flash. -->
    <#if properties.scripts?has_content>
        <#list properties.scripts?split(' ') as script>
            <script src="${url.resourcesPath}/${script}" type="text/javascript"></script>
        </#list>
    </#if>
    <script type="importmap">
        {
            "imports": {
                "rfc4648": "${url.resourcesCommonPath}/vendor/rfc4648/rfc4648.js"
            }
        }
    </script>
    <#if scripts??>
        <#list scripts as script>
            <script src="${script}" type="text/javascript"></script>
        </#list>
    </#if>
    <script type="module">
        <#outputformat "JavaScript">
        import { startSessionPolling } from ${(url.resourcesPath + "/js/authChecker.js")?c};

        startSessionPolling(
            ${url.ssoLoginInOtherTabsUrl?c}
        );
        </#outputformat>
    </script>
    <#if authenticationSession??>
        <script type="module">
            <#outputformat "JavaScript">
            import { checkAuthSession } from ${(url.resourcesPath + "/js/authChecker.js")?c};

            checkAuthSession(
                ${authenticationSession.authSessionIdHash?c}
            );
            </#outputformat>
        </script>
    </#if>
</head>

<body class="${bodyClass}" data-page-id="login-${pageId}">
<div class="rc-shell">
    <aside class="rc-brand">
        <div>
            <div class="rc-tag" data-brand-tag data-pattern="${msg('recuroWorkspaceTag')}" hidden></div>
            <h1 data-brand-tagline>${msg("recuroBrandPitch")}</h1>
            <ul data-brand-points>
                <li>${msg("recuroPointOne")}</li>
                <li>${msg("recuroPointTwo")}</li>
                <li>${msg("recuroPointThree")}</li>
            </ul>
        </div>
        <div class="rc-powered">
            <div class="rc-logo">
                <svg width="34" height="34" viewBox="0 0 96 96" role="img" aria-label="Recuro">
                    <defs>
                        <linearGradient id="rcG" x1="0" y1="0" x2="1" y2="1">
                            <stop offset="0" stop-color="#818CF8"/>
                            <stop offset=".55" stop-color="#A78BFA"/>
                            <stop offset="1" stop-color="#22D3EE"/>
                        </linearGradient>
                    </defs>
                    <rect width="96" height="96" rx="24" fill="url(#rcG)"/>
                    <g stroke="#141C42" stroke-width="9.5" stroke-linecap="round" stroke-linejoin="round" fill="none">
                        <path d="M33 28 V70"/>
                        <path d="M33 28 H49 a13 13 0 0 1 0 26 H33"/>
                        <path d="M50 54 L66 70 M57.5 70 H66 V61.5"/>
                    </g>
                </svg>
                <div class="rc-wordmark">
                    <div>Recu<span>ro</span></div>
                    <small>RECRUITMENT, UNINTERRUPTED</small>
                </div>
            </div>
            <span data-brand-powered hidden>${msg("recuroPoweredBy")}</span>
        </div>
    </aside>

    <main class="rc-main">
        <div class="rc-card">
            <div class="rc-step">
                <#if showHeader>
                    <h2 id="kc-page-title"><#nested "header"></h2>
                    <#if auth?has_content && auth.showUsername() && !auth.showResetCredentials()>
                        <#nested "show-username">
                        <div class="rc-chip">
                            <span class="rc-chip-mark person" aria-hidden="true">${(auth.attemptedUsername!'?')?substring(0, 1)?upper_case}</span>
                            <span class="rc-chip-text"><small>${msg("recuroStepEmail")}</small><b>${auth.attemptedUsername}</b></span>
                            <a class="rc-chip-change" href="${url.loginRestartFlowUrl}" aria-label="${msg('restartLoginTooltip')}">${msg("recuroChange")}</a>
                        </div>
                    </#if>
                </#if>

                <#-- App-initiated actions should not see warnings about the action they are completing. -->
                <#if displayMessage && message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
                    <div class="rc-alert rc-alert-${message.type}" role="<#if message.type = 'error'>alert<#else>status</#if>">
                        <span class="rc-alert-text">${kcSanitize(message.summary)?no_esc}</span>
                    </div>
                </#if>

                <#nested "form">

                <#if auth?has_content && auth.showTryAnotherWayLink()>
                    <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post">
                        <input type="hidden" name="tryAnotherWay" value="on"/>
                        <button type="submit" class="rc-link-button">${msg("doTryAnotherWay")}</button>
                    </form>
                </#if>

                <#nested "socialProviders">

                <#if displayInfo>
                    <div id="kc-info" class="rc-alt">
                        <#nested "info">
                    </div>
                </#if>
            </div>
        </div>
    </main>
</div>
</body>
</html>
</#macro>
