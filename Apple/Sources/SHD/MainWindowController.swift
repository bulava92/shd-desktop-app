import Cocoa
import WebKit
import AuthenticationServices
import CryptoKit
import Security

@MainActor
private final class WeakScriptMessageHandler: NSObject, WKScriptMessageHandler {
    weak var controller: MainWindowController?
    
    init(controller: MainWindowController) {
        self.controller = controller
    }
    
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        if message.name == "shdDesktopAuth" {
            let origin = message.frameInfo.securityOrigin
            let trustedHosts = ["shd.xyz.su", "dev-shd.xyz.su"]
            guard message.frameInfo.isMainFrame,
                  origin.protocol.lowercased() == "https",
                  trustedHosts.contains(origin.host.lowercased()) else {
                return
            }
            let environment: MainWindowController.Environment = origin.host.lowercased() == "dev-shd.xyz.su" ? .dev : .production
            controller?.signInWithPasskeyInBrowser(for: environment)
            return
        }

        if message.name == "shdNativePrint" {
            controller?.printCurrentPage()
        }
    }
}

@MainActor
final class MainWindowController: NSWindowController, NSWindowDelegate, WKNavigationDelegate, WKUIDelegate, WKDownloadDelegate, ASWebAuthenticationPresentationContextProviding {
    
    enum Environment: String, CaseIterable {
        case production = "https://shd.xyz.su"
        case dev = "https://dev-shd.xyz.su"
        
        var title: String {
            switch self {
            case .production: return "SHD Prod"
            case .dev: return "SHD Dev"
            }
        }
    }
    
    var webView: WKWebView!
    private var progressIndicator: NSProgressIndicator!
    private var currentEnv: Environment = .production
    private var observation: NSKeyValueObservation?
    private var authSession: ASWebAuthenticationSession?
    private var authAttemptID: UUID?

    private static let authClientID = "shd-desktop"
    private static let authCallbackScheme = "shd-auth"
    private static let authCallbackURI = "shd-auth://callback"
    
    init(initialEnv: Environment? = nil, initialRequest: URLRequest? = nil) {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1440, height: 920),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.center()
        window.title = "SHD"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = false
        window.isReleasedWhenClosed = false
        window.minSize = NSSize(width: 800, height: 500)
        
        // Dark theme: ensures window and native tabs are always dark matching SHD
        window.appearance = NSAppearance(named: .darkAqua)
        let darkBg = NSColor(calibratedRed: 0.08, green: 0.08, blue: 0.08, alpha: 1.0)
        window.backgroundColor = darkBg
        
        // Native macOS tabs support
        window.tabbingMode = .preferred
        window.tabbingIdentifier = "SHDMainTabGroup"
        
        super.init(window: window)
        
        window.delegate = self
        setupWebView(darkBg: darkBg)
        
        if let initialRequest = initialRequest {
            webView.load(initialRequest)
        } else if let initialEnv = initialEnv {
            currentEnv = initialEnv
            navigateToEnvironment(currentEnv)
        } else {
            loadInitialEnvironment()
        }
    }
    
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }
    
    private func setupWebView(darkBg: NSColor) {
        guard let window = window else { return }
        
        let config = WKWebViewConfiguration()
        config.websiteDataStore = WKWebsiteDataStore.default()
        config.preferences.isElementFullscreenEnabled = true
        config.defaultWebpagePreferences.allowsContentJavaScript = true
        
        // Keep page colors under the website's control. Only bridge printing
        // to the native macOS print dialog.
        let darkScript = """
        (function() {
            window.print = function() {
                if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.shdNativePrint) {
                    window.webkit.messageHandlers.shdNativePrint.postMessage({});
                }
            };
        })();
        """
        let userScript = WKUserScript(source: darkScript, injectionTime: .atDocumentStart, forMainFrameOnly: false)
        config.userContentController.addUserScript(userScript)

        let passkeyBridgeScript = """
        (function() {
            document.addEventListener('click', function(event) {
                var path = window.location.pathname;
                while (path.length > 1 && path.endsWith('/')) {
                    path = path.slice(0, -1);
                }
                if (path !== '/login' && path !== '/login/two-factor') {
                    return;
                }

                var target = event.target instanceof Element
                    ? event.target
                    : (event.target && event.target.parentElement);
                if (!target || !target.closest('#btnPasskeyLogin, #twoFactorPasskey')) {
                    return;
                }

                event.preventDefault();
                event.stopPropagation();
                event.stopImmediatePropagation();

                var bridge = window.webkit && window.webkit.messageHandlers
                    ? window.webkit.messageHandlers.shdDesktopAuth
                    : null;
                if (bridge) {
                    bridge.postMessage('passkey-login');
                }
            }, true);
        })();
        """
        config.userContentController.addUserScript(
            WKUserScript(source: passkeyBridgeScript, injectionTime: .atDocumentStart, forMainFrameOnly: true)
        )
        config.userContentController.add(WeakScriptMessageHandler(controller: self), name: "shdNativePrint")
        config.userContentController.add(WeakScriptMessageHandler(controller: self), name: "shdDesktopAuth")
        
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        container.wantsLayer = true
        container.layer?.backgroundColor = darkBg.cgColor
        window.contentView = container
        
        webView = WKWebView(frame: .zero, configuration: config)
        webView.translatesAutoresizingMaskIntoConstraints = false
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.allowsBackForwardNavigationGestures = true
        webView.isInspectable = true
        webView.underPageBackgroundColor = darkBg
        webView.setValue(false, forKey: "drawsBackground") // Prevents WebKit initial white canvas
        container.addSubview(webView)
        
        progressIndicator = NSProgressIndicator()
        progressIndicator.isIndeterminate = false
        progressIndicator.style = .bar
        progressIndicator.minValue = 0.0
        progressIndicator.maxValue = 1.0
        progressIndicator.translatesAutoresizingMaskIntoConstraints = false
        progressIndicator.isDisplayedWhenStopped = false
        container.addSubview(progressIndicator)
        
        // Keep the web page below the native titlebar and tab strip. The page's
        // fixed header and background then use the unobstructed web viewport.
        NSLayoutConstraint.activate([
            webView.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            webView.topAnchor.constraint(equalTo: container.safeAreaLayoutGuide.topAnchor),
            webView.bottomAnchor.constraint(equalTo: container.bottomAnchor),
            
            progressIndicator.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            progressIndicator.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            progressIndicator.topAnchor.constraint(equalTo: container.safeAreaLayoutGuide.topAnchor),
            progressIndicator.heightAnchor.constraint(equalToConstant: 2.0)
        ])
        
        observation = webView.observe(\.estimatedProgress, options: [.new]) { [weak self] webView, _ in
            Task { @MainActor [weak self] in
                guard let self = self else { return }
                self.progressIndicator.doubleValue = webView.estimatedProgress
                if webView.estimatedProgress >= 1.0 {
                    self.progressIndicator.stopAnimation(nil)
                    self.progressIndicator.isHidden = true
                } else {
                    self.progressIndicator.isHidden = false
                    self.progressIndicator.startAnimation(nil)
                }
                self.updateTitle()
            }
        }
    }
    
    private func loadInitialEnvironment() {
        let savedEnvRaw = UserDefaults.standard.string(forKey: "SHDSelectedEnvironment")
        if let savedEnvRaw = savedEnvRaw, let env = Environment(rawValue: savedEnvRaw) {
            currentEnv = env
        } else {
            currentEnv = .production
        }
        navigateToEnvironment(currentEnv)
    }
    
    func navigateToEnvironment(_ env: Environment) {
        currentEnv = env
        UserDefaults.standard.set(env.rawValue, forKey: "SHDSelectedEnvironment")
        if let url = URL(string: env.rawValue) {
            let request = URLRequest(url: url)
            webView.load(request)
        }
        updateTitle()
    }

    func signInWithPasskeyInBrowser(for environment: Environment) {
        guard authSession == nil else { return }

        guard environment == .dev else {
            presentDesktopAuthError("Вход по passkey в приложении пока подключён только для DEV.")
            return
        }

        do {
            let verifier = try Self.randomBase64URLString(byteCount: 32)
            let state = try Self.randomBase64URLString(byteCount: 32)
            let challenge = Self.base64URLString(for: Data(SHA256.hash(data: Data(verifier.utf8))))

            guard let authorizationURL = makeAuthorizationURL(
                environment: environment,
                state: state,
                challenge: challenge
            ) else {
                presentDesktopAuthError("Не удалось подготовить адрес входа.")
                return
            }

            let attemptID = UUID()
            authAttemptID = attemptID

            let completionHandler: ASWebAuthenticationSession.CompletionHandler = { [weak self] callbackURL, error in
                Task { @MainActor [weak self] in
                    self?.finishBrowserPasskeyLogin(
                        callbackURL: callbackURL,
                        error: error,
                        expectedState: state,
                        verifier: verifier,
                        environment: environment,
                        attemptID: attemptID
                    )
                }
            }
            let session: ASWebAuthenticationSession
            if #available(macOS 14.4, *) {
                session = ASWebAuthenticationSession(
                    url: authorizationURL,
                    callback: .customScheme(Self.authCallbackScheme),
                    completionHandler: completionHandler
                )
            } else {
                session = ASWebAuthenticationSession(
                    url: authorizationURL,
                    callbackURLScheme: Self.authCallbackScheme,
                    completionHandler: completionHandler
                )
            }
            session.presentationContextProvider = self
            authSession = session

            if !session.start() {
                authSession = nil
                authAttemptID = nil
                presentDesktopAuthError("macOS не смог открыть окно входа. Попробуйте ещё раз.")
            }
        } catch {
            presentDesktopAuthError("Не удалось безопасно подготовить вход.")
        }
    }

    private func makeAuthorizationURL(environment: Environment, state: String, challenge: String) -> URL? {
        guard var components = URLComponents(string: environment.rawValue) else { return nil }
        components.path = "/desktop-auth/authorize"
        components.queryItems = [
            URLQueryItem(name: "client_id", value: Self.authClientID),
            URLQueryItem(name: "redirect_uri", value: Self.authCallbackURI),
            URLQueryItem(name: "response_type", value: "code"),
            URLQueryItem(name: "state", value: state),
            URLQueryItem(name: "code_challenge", value: challenge),
            URLQueryItem(name: "code_challenge_method", value: "S256")
        ]
        return components.url
    }

    private func finishBrowserPasskeyLogin(
        callbackURL: URL?,
        error: Error?,
        expectedState: String,
        verifier: String,
        environment: Environment,
        attemptID: UUID
    ) {
        guard authAttemptID == attemptID else { return }
        authAttemptID = nil
        authSession = nil

        if let error {
            if (error as? ASWebAuthenticationSessionError)?.code != .canceledLogin {
                presentDesktopAuthError("Вход через браузер не завершился. Попробуйте ещё раз.")
            }
            return
        }

        guard let callbackURL,
              callbackURL.scheme?.lowercased() == Self.authCallbackScheme,
              callbackURL.host?.lowercased() == "callback",
              let callbackComponents = URLComponents(url: callbackURL, resolvingAgainstBaseURL: false),
              let queryItems = callbackComponents.queryItems else {
            presentDesktopAuthError("Сервер вернул некорректный ответ входа.")
            return
        }

        if queryItems.contains(where: { $0.name == "error" }) {
            presentDesktopAuthError("Сервер не завершил вход. Попробуйте ещё раз.")
            return
        }

        let stateValues = queryItems.filter { $0.name == "state" }
        let codeValues = queryItems.filter { $0.name == "code" }
        guard stateValues.count == 1,
              stateValues[0].value == expectedState,
              codeValues.count == 1,
              let code = codeValues[0].value,
              !code.isEmpty else {
            presentDesktopAuthError("Не удалось подтвердить ответ сервера. Начните вход заново.")
            return
        }

        guard var completeComponents = URLComponents(string: environment.rawValue) else {
            presentDesktopAuthError("Не удалось завершить вход в выбранном контуре.")
            return
        }
        completeComponents.path = "/desktop-auth/complete"
        guard let completeURL = completeComponents.url else {
            presentDesktopAuthError("Не удалось завершить вход в выбранном контуре.")
            return
        }

        var request = URLRequest(url: completeURL, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
        request.httpMethod = "POST"
        request.setValue("application/x-www-form-urlencoded; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.setValue("no-store", forHTTPHeaderField: "Cache-Control")
        request.httpBody = Self.formEncodedBody([
            URLQueryItem(name: "code", value: code),
            URLQueryItem(name: "code_verifier", value: verifier)
        ])

        currentEnv = environment
        UserDefaults.standard.set(environment.rawValue, forKey: "SHDSelectedEnvironment")
        webView.load(request)
    }

    private func presentDesktopAuthError(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "Вход в SHD"
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }

    func presentationAnchor(for session: ASWebAuthenticationSession) -> ASPresentationAnchor {
        window ?? NSApp.keyWindow ?? NSWindow()
    }

    private static func randomBase64URLString(byteCount: Int) throws -> String {
        var bytes = [UInt8](repeating: 0, count: byteCount)
        let status = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
        guard status == errSecSuccess else {
            throw DesktopAuthError.randomNumberGenerationFailed
        }
        return base64URLString(for: Data(bytes))
    }

    private static func base64URLString(for data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    private static func formEncodedBody(_ items: [URLQueryItem]) -> Data {
        var components = URLComponents()
        components.queryItems = items
        return Data((components.percentEncodedQuery ?? "").utf8)
    }

    private enum DesktopAuthError: Error {
        case randomNumberGenerationFailed
    }
    
    func reload() {
        webView.reload()
    }
    
    func reloadIgnoringCache() {
        webView.reloadFromOrigin()
    }
    
    func goBack() {
        if webView.canGoBack {
            webView.goBack()
        }
    }
    
    func goForward() {
        if webView.canGoForward {
            webView.goForward()
        }
    }
    
    func zoomIn() {
        webView.pageZoom += 0.1
    }
    
    func zoomOut() {
        if webView.pageZoom > 0.4 {
            webView.pageZoom -= 0.1
        }
    }
    
    func resetZoom() {
        webView.pageZoom = 1.0
    }
    
    func showInspector() {
        if let inspector = webView.value(forKey: "_inspector") as? NSObject {
            inspector.perform(Selector(("show")))
        }
    }
    
    func showConsole() {
        if let inspector = webView.value(forKey: "_inspector") as? NSObject {
            inspector.perform(Selector(("showConsole")))
        }
    }
    
    func printCurrentPage() {
        guard let window = window else { return }
        let printInfo = NSPrintInfo.shared.copy() as? NSPrintInfo ?? NSPrintInfo.shared
        printInfo.horizontalPagination = .fit
        printInfo.verticalPagination = .automatic
        printInfo.isHorizontallyCentered = true
        printInfo.isVerticallyCentered = false
        
        let printOp = webView.printOperation(with: printInfo)
        printOp.view?.frame = webView.bounds
        printOp.showsPrintPanel = true
        printOp.showsProgressPanel = true
        printOp.runModal(for: window, delegate: nil, didRun: nil, contextInfo: nil)
    }
    
    private func cleanTitle(from raw: String) -> String {
        // Strip out repetitive brand suffixes for clean, elegant tab titles
        var title = raw
        if let range = title.range(of: " — SHD Project") {
            title = String(title[..<range.lowerBound])
        } else if let range = title.range(of: " - SHD Project") {
            title = String(title[..<range.lowerBound])
        } else if let range = title.range(of: " | SHD") {
            title = String(title[..<range.lowerBound])
        }
        title = title.trimmingCharacters(in: .whitespacesAndNewlines)
        return title.isEmpty ? "SHD" : title
    }
    
    private func updateTitle() {
        guard let window = window else { return }
        let raw = webView.title ?? ""
        let clean = cleanTitle(from: raw)
        window.title = clean
    }
    
    // MARK: - NSWindowDelegate
    
    func windowWillClose(_ notification: Notification) {
        authAttemptID = nil
        authSession?.cancel()
        authSession = nil
        webView.configuration.userContentController.removeScriptMessageHandler(forName: "shdNativePrint")
        webView.configuration.userContentController.removeScriptMessageHandler(forName: "shdDesktopAuth")
        AppDelegate.shared?.removeWindowController(self)
    }
    
    // MARK: - WKNavigationDelegate
    
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        updateTitle()
    }
    
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, preferences: WKWebpagePreferences) async -> (WKNavigationActionPolicy, WKWebpagePreferences) {
        if navigationAction.shouldPerformDownload {
            return (.download, preferences)
        }
        
        guard let url = navigationAction.request.url else {
            return (.allow, preferences)
        }
        
        let host = url.host?.lowercased() ?? ""
        let isInternalHost = host == "shd.xyz.su" || host == "dev-shd.xyz.su" || host == "sso.xyz.su" || host == "git.xyz.su" || host.hasSuffix(".xyz.su")
        
        if navigationAction.navigationType == .linkActivated {
            let openInNewTab = navigationAction.modifierFlags.contains(.command) || navigationAction.buttonNumber == 2
            if isInternalHost {
                if openInNewTab {
                    AppDelegate.shared?.openTab(with: navigationAction.request, relativeTo: self.window)
                    return (.cancel, preferences)
                }
                return (.allow, preferences)
            } else {
                NSWorkspace.shared.open(url)
                return (.cancel, preferences)
            }
        }
        
        if isInternalHost {
            return (.allow, preferences)
        }
        
        return (.allow, preferences)
    }
    
    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse) async -> WKNavigationResponsePolicy {
        if navigationResponse.canShowMIMEType {
            return .allow
        }
        return .download
    }
    
    // MARK: - WKDownloadDelegate
    
    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) {
        download.delegate = self
    }
    
    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) {
        download.delegate = self
    }
    
    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse, suggestedFilename: String) async -> URL? {
        let downloadsDir = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first ?? URL(fileURLWithPath: NSHomeDirectory())
        var destination = downloadsDir.appendingPathComponent(suggestedFilename)
        
        var counter = 1
        let baseName = (suggestedFilename as NSString).deletingPathExtension
        let ext = (suggestedFilename as NSString).pathExtension
        
        while FileManager.default.fileExists(atPath: destination.path) {
            let newName = ext.isEmpty ? "\(baseName) (\(counter))" : "\(baseName) (\(counter)).\(ext)"
            destination = downloadsDir.appendingPathComponent(newName)
            counter += 1
        }
        
        return destination
    }
    
    func downloadDidFinish(_ download: WKDownload) {
        NSSound(named: "Glass")?.play()
    }
    
    // MARK: - WKUIDelegate (Native Alerts, Confirms, Prompts, Media Permissions & File Uploads)
    
    func webView(_ webView: WKWebView, decideMediaCapturePermissionsFor origin: WKSecurityOrigin, initiatedBy frame: WKFrameInfo, type: WKMediaCaptureType) async -> WKPermissionDecision {
        let host = origin.host.lowercased()
        if host == "shd.xyz.su" || host == "dev-shd.xyz.su" || host.hasSuffix(".xyz.su") {
            return .grant
        }
        return .prompt
    }
    
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if navigationAction.targetFrame == nil {
            if let url = navigationAction.request.url {
                let host = url.host?.lowercased() ?? ""
                if host == "shd.xyz.su" || host == "dev-shd.xyz.su" || host == "sso.xyz.su" || host == "git.xyz.su" || host.hasSuffix(".xyz.su") {
                    AppDelegate.shared?.openTab(with: navigationAction.request, relativeTo: self.window)
                } else {
                    NSWorkspace.shared.open(url)
                }
            }
        }
        return nil
    }
    
    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo) async {
        let alert = NSAlert()
        alert.messageText = "SHD"
        alert.informativeText = message
        alert.alertStyle = .informational
        alert.addButton(withTitle: "OK")
        if let window = window {
            _ = await alert.beginSheetModal(for: window)
        } else {
            alert.runModal()
        }
    }
    
    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo) async -> Bool {
        let alert = NSAlert()
        alert.messageText = "SHD"
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Подтвердить")
        alert.addButton(withTitle: "Отмена")
        
        let response: NSApplication.ModalResponse
        if let window = window {
            response = await alert.beginSheetModal(for: window)
        } else {
            response = alert.runModal()
        }
        return response == .alertFirstButtonReturn
    }
    
    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String, defaultText: String?, initiatedByFrame frame: WKFrameInfo) async -> String? {
        let alert = NSAlert()
        alert.messageText = "SHD"
        alert.informativeText = prompt
        alert.addButton(withTitle: "OK")
        alert.addButton(withTitle: "Отмена")
        
        let input = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        input.stringValue = defaultText ?? ""
        alert.accessoryView = input
        
        let response: NSApplication.ModalResponse
        if let window = window {
            response = await alert.beginSheetModal(for: window)
        } else {
            response = alert.runModal()
        }
        
        if response == .alertFirstButtonReturn {
            return input.stringValue
        }
        return nil
    }
    
    func webView(_ webView: WKWebView, runOpenPanelWith parameters: WKOpenPanelParameters, initiatedByFrame frame: WKFrameInfo) async -> [URL]? {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = parameters.allowsMultipleSelection
        panel.canChooseDirectories = parameters.allowsDirectories
        panel.canChooseFiles = true
        
        let response: NSApplication.ModalResponse
        if let window = window {
            response = await panel.beginSheetModal(for: window)
        } else {
            response = panel.runModal()
        }
        
        if response == .OK {
            return panel.urls
        }
        return nil
    }
}
