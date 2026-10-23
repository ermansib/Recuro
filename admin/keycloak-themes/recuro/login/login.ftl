<#-- The password step of the portal's sign-in card (RCU-PLT-001). The portal has already asked for the
     workspace and the email (sent here as login_hint), so this page shows both as chips with a way back,
     and asks only for the password. Opened without an email, the same card asks for the email first and
     then the password (js/recuro.js switches the steps in place, no second page); without JavaScript both
     fields show together. -->
<#import "template.ftl" as layout>
<#import "passkeys.ftl" as passkeys>
<#assign credentialsError = messagesPerField.existsError('username','password')>
<#assign knownEmail = (login.username!'')?has_content && !usernameHidden??>
<@layout.registrationLayout displayMessage=false showHeader=false displayInfo=realm.password && realm.registrationAllowed && !registrationDisabled??; section>
    <#if section = "form">
        <a class="rc-back" data-portal-back data-email="${login.username!''}" href="#" hidden>
            <svg viewBox="0 0 20 20" width="16" height="16" aria-hidden="true"><path d="M12.5 4.5 7 10l5.5 5.5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
            ${msg("recuroBack")}
        </a>
        <#if knownEmail>
            <ol class="rc-steps" aria-label="${msg('recuroStepsLabel')}">
                <#-- The workspace step only shows when the portal sent the workspace along (js/recuro.js). -->
                <li class="done" data-brand-only hidden><span class="rc-steps-dot" aria-hidden="true"></span><span>${msg("recuroStepWorkspace")}</span></li>
                <li class="done"><span class="rc-steps-dot" aria-hidden="true"></span><span>${msg("recuroStepEmail")}</span></li>
                <li class="on" aria-current="step"><span class="rc-steps-dot" aria-hidden="true"></span><span>${msg("recuroStepPassword")}</span></li>
            </ol>
            <h2 id="kc-page-title">${msg("loginAccountTitle")}</h2>
            <p class="rc-sub">${msg("recuroPasswordSub")}</p>
        <#else>
            <#-- Email first, then password, in this one card. js/recuro.js moves the step markers and swaps the
                 heading to the data-password-* text when the person presses Continue. -->
            <ol class="rc-steps" aria-label="${msg('recuroStepsLabelTwo')}">
                <li class="on" aria-current="step" data-step-mark="email"><span class="rc-steps-dot" aria-hidden="true"></span><span>${msg("recuroStepEmail")}</span></li>
                <li data-step-mark="password"><span class="rc-steps-dot" aria-hidden="true"></span><span>${msg("recuroStepPassword")}</span></li>
            </ol>
            <h2 id="kc-page-title" data-password-text="${msg('loginAccountTitle')}">${msg("recuroEmailTitle")}</h2>
            <p class="rc-sub" data-password-text="${msg('recuroPasswordSub')}">${msg("recuroEmailSub")}</p>
        </#if>

        <div class="rc-who">
            <div class="rc-chip" data-brand-chip hidden>
                <span class="rc-chip-mark" aria-hidden="true" data-brand-initial></span>
                <span class="rc-chip-text"><small>${msg("recuroStepWorkspace")}</small><b data-brand-name></b></span>
            </div>
            <#if knownEmail>
                <div class="rc-chip">
                    <span class="rc-chip-mark person" aria-hidden="true">${login.username?substring(0, 1)?upper_case}</span>
                    <span class="rc-chip-text"><small>${msg("recuroStepEmail")}</small><b>${login.username}</b></span>
                    <a class="rc-chip-change" data-portal-back data-email="${login.username}" href="${url.loginRestartFlowUrl}" aria-label="${msg('recuroChangeEmail')}">${msg("recuroChange")}</a>
                </div>
            <#elseif !usernameHidden??>
                <#-- Filled in and shown by js/recuro.js once the email step is done. -->
                <div class="rc-chip" data-email-chip hidden>
                    <span class="rc-chip-mark person" aria-hidden="true" data-email-initial></span>
                    <span class="rc-chip-text"><small>${msg("recuroStepEmail")}</small><b data-email-value></b></span>
                    <button class="rc-chip-change rc-link-button" type="button" data-change-email aria-label="${msg('recuroChangeEmail')}">${msg("recuroChange")}</button>
                </div>
            </#if>
        </div>

        <#if message?has_content && !credentialsError && (message.type != 'warning' || !isAppInitiatedAction??)>
            <div class="rc-alert rc-alert-${message.type}" role="<#if message.type = 'error'>alert<#else>status</#if>">
                <span class="rc-alert-text">${kcSanitize(message.summary)?no_esc}</span>
            </div>
        </#if>

        <#if realm.password>
            <form id="kc-form-login" class="rc-form" data-signing-in="${msg('recuroSigningIn')}" action="${url.loginAction}" method="post" novalidate
                  <#if !knownEmail && !usernameHidden??>data-identifier-first data-email-invalid="${msg('recuroEmailInvalid')}"</#if>>
                <#if knownEmail>
                    <#-- Sent with the password, and lets password managers pair the two. -->
                    <input class="rc-sr-only" id="username" name="username" type="email" value="${login.username}" autocomplete="username" readonly tabindex="-1" aria-hidden="true"/>
                <#elseif !usernameHidden??>
                    <div class="rc-group<#if credentialsError> rc-err</#if>" data-step="email">
                        <label for="username" class="rc-label">${msg("email")}</label>
                        <input id="username" class="rc-input" name="username" value="${(login.username!'')}" type="email" inputmode="email"
                               autofocus autocomplete="${(enableWebAuthnConditionalUI?has_content)?then('username webauthn', 'username')}"
                               aria-invalid="${credentialsError?c}" aria-describedby="email-error" dir="ltr"/>
                        <span id="email-error" class="rc-field-error" role="alert" data-email-error hidden></span>
                        <button class="rc-btn rc-btn-primary rc-btn-block rc-continue" type="button" data-continue hidden>${msg("recuroContinue")}</button>
                    </div>
                </#if>

                <div class="rc-form" data-step="password">
                <div class="rc-group<#if credentialsError> rc-err</#if>">
                    <label for="password" class="rc-label">${msg("password")}</label>
                    <div class="rc-input-group" dir="ltr">
                        <input id="password" class="rc-input" name="password" type="password" autocomplete="current-password"
                               <#if knownEmail || usernameHidden??>autofocus</#if>
                               aria-invalid="${credentialsError?c}" <#if credentialsError>aria-describedby="input-error"</#if>/>
                        <button class="rc-pw-toggle" type="button" aria-label="${msg('showPassword')}" aria-controls="password" data-password-toggle
                                data-icon-show="rc-eye" data-icon-hide="rc-eye rc-eye-off"
                                data-label-show="${msg('showPassword')}" data-label-hide="${msg('hidePassword')}">
                            <i class="rc-eye" aria-hidden="true"></i>
                        </button>
                    </div>
                    <#if credentialsError>
                        <span id="input-error" class="rc-field-error" role="alert">
                            ${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}
                        </span>
                    </#if>
                </div>

                <div class="rc-row">
                    <#if realm.rememberMe && !usernameHidden??>
                        <label class="rc-check">
                            <input id="rememberMe" name="rememberMe" type="checkbox" <#if login.rememberMe??>checked</#if>> ${msg("rememberMe")}
                        </label>
                    </#if>
                    <#if realm.resetPasswordAllowed>
                        <a href="${url.loginResetCredentialsUrl}">${msg("doForgotPassword")}</a>
                    </#if>
                </div>

                <input type="hidden" id="id-hidden-input" name="credentialId" <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>/>
                <button class="rc-btn rc-btn-primary rc-btn-block" name="login" id="kc-login" type="submit">${msg("doLogIn")}</button>
                </div>
            </form>
        </#if>
        <@passkeys.conditionalUIData />
        <script type="module" src="${url.resourcesPath}/js/passwordVisibility.js"></script>
    <#elseif section = "info">
        <#if realm.password && realm.registrationAllowed && !registrationDisabled??>
            <span>${msg("noAccount")} <a href="${url.registrationUrl}">${msg("doRegister")}</a></span>
        </#if>
    <#elseif section = "socialProviders">
        <#if realm.password && social?? && social.providers?has_content>
            <div class="rc-social">
                <div class="rc-divider"><span>${msg("identity-provider-login-label")}</span></div>
                <#list social.providers as p>
                    <a id="social-${p.alias}" class="rc-btn rc-btn-ghost rc-btn-block" href="${p.loginUrl}">${p.displayName!}</a>
                </#list>
            </div>
        </#if>
    </#if>
</@layout.registrationLayout>
