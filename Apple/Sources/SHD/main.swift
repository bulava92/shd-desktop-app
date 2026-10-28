import Cocoa
import UserNotifications

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, UNUserNotificationCenterDelegate {
    static weak var shared: AppDelegate?
    
    private(set) var windowControllers: [MainWindowController] = []
    
    override init() {
        super.init()
        AppDelegate.shared = self
    }
    
    func applicationDidFinishLaunching(_ notification: Notification) {
        setupMainMenu()
        setupNotifications()
        
        let initialController = MainWindowController()
        addWindowController(initialController)
        initialController.showWindow(nil)
        NSApp.activate(ignoringOtherApps: true)
    }
    
    func addWindowController(_ controller: MainWindowController) {
        if !windowControllers.contains(where: { $0 === controller }) {
            windowControllers.append(controller)
        }
    }
    
    func removeWindowController(_ controller: MainWindowController) {
        windowControllers.removeAll(where: { $0 === controller })
    }
    
    private func setupNotifications() {
        let center = UNUserNotificationCenter.current()
        center.delegate = self
        center.requestAuthorization(options: [.alert, .badge, .sound]) { _, _ in }
    }
    
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .badge, .sound])
    }
    
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag {
            if let first = windowControllers.first {
                first.showWindow(nil)
            } else {
                let controller = MainWindowController()
                addWindowController(controller)
                controller.showWindow(nil)
            }
        }
        return true
    }
    
    private func setupMainMenu() {
        let mainMenu = NSMenu()
        
        // App Menu
        let appMenuItem = NSMenuItem()
        let appMenu = NSMenu(title: "SHD")
        appMenu.addItem(withTitle: "О программе SHD", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(NSMenuItem.separator())
        appMenu.addItem(withTitle: "Скрыть SHD", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        let hideOthersItem = appMenu.addItem(withTitle: "Скрыть остальные", action: #selector(NSApplication.hideOtherApplications(_:)), keyEquivalent: "h")
        hideOthersItem.keyEquivalentModifierMask = [.command, .option]
        appMenu.addItem(withTitle: "Показать все", action: #selector(NSApplication.unhideAllApplications(_:)), keyEquivalent: "")
        appMenu.addItem(NSMenuItem.separator())
        appMenu.addItem(withTitle: "Завершить SHD", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appMenuItem.submenu = appMenu
        mainMenu.addItem(appMenuItem)
        
        // File Menu (New Tab, New Window, Print, Close)
        let fileMenuItem = NSMenuItem()
        let fileMenu = NSMenu(title: "Файл")
        fileMenu.addItem(withTitle: "Новая вкладка", action: #selector(onNewTab), keyEquivalent: "t")
        fileMenu.addItem(withTitle: "Новое окно", action: #selector(onNewWindow), keyEquivalent: "n")
        fileMenu.addItem(NSMenuItem.separator())
        fileMenu.addItem(withTitle: "Напечатать…", action: #selector(onPrint), keyEquivalent: "p")
        fileMenu.addItem(NSMenuItem.separator())
        fileMenu.addItem(withTitle: "Закрыть вкладку", action: #selector(onCloseTabOrWindow), keyEquivalent: "w")
        fileMenuItem.submenu = fileMenu
        mainMenu.addItem(fileMenuItem)
        
        // Edit Menu
        let editMenuItem = NSMenuItem()
        let editMenu = NSMenu(title: "Правка")
        editMenu.addItem(withTitle: "Отменить", action: Selector(("undo:")), keyEquivalent: "z")
        let redoItem = editMenu.addItem(withTitle: "Повторить", action: Selector(("redo:")), keyEquivalent: "z")
        redoItem.keyEquivalentModifierMask = [.command, .shift]
        editMenu.addItem(NSMenuItem.separator())
        editMenu.addItem(withTitle: "Вырезать", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Скопировать", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Вставить", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Выбрать всё", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editMenuItem.submenu = editMenu
        mainMenu.addItem(editMenuItem)
        
        // View Menu
        let viewMenuItem = NSMenuItem()
        let viewMenu = NSMenu(title: "Вид")
        viewMenu.addItem(withTitle: "Обновить", action: #selector(onReload), keyEquivalent: "r")
        let hardReload = viewMenu.addItem(withTitle: "Очистить кэш и обновить", action: #selector(onHardReload), keyEquivalent: "r")
        hardReload.keyEquivalentModifierMask = [.command, .shift]
        viewMenu.addItem(NSMenuItem.separator())
        viewMenu.addItem(withTitle: "Увеличить", action: #selector(onZoomIn), keyEquivalent: "+")
        viewMenu.addItem(withTitle: "Уменьшить", action: #selector(onZoomOut), keyEquivalent: "-")
        viewMenu.addItem(withTitle: "Исходный масштаб", action: #selector(onResetZoom), keyEquivalent: "0")
        viewMenu.addItem(NSMenuItem.separator())
        viewMenu.addItem(withTitle: "Полноэкранный режим", action: #selector(NSWindow.toggleFullScreen(_:)), keyEquivalent: "f")
        viewMenu.addItem(NSMenuItem.separator())
        let inspectorItem = viewMenu.addItem(withTitle: "Инспектор веб-страницы", action: #selector(onShowInspector), keyEquivalent: "i")
        inspectorItem.keyEquivalentModifierMask = [.command, .option]
        let consoleItem = viewMenu.addItem(withTitle: "Консоль JavaScript", action: #selector(onShowConsole), keyEquivalent: "c")
        consoleItem.keyEquivalentModifierMask = [.command, .option]
        viewMenuItem.submenu = viewMenu
        mainMenu.addItem(viewMenuItem)

        // Environment Menu
        let envMenuItem = NSMenuItem()
        let envMenu = NSMenu(title: "Контур")
        let prodItem = envMenu.addItem(withTitle: "SHD Production (shd.xyz.su)", action: #selector(onSelectProd), keyEquivalent: "1")
        prodItem.target = self
        let devItem = envMenu.addItem(withTitle: "SHD Dev (dev-shd.xyz.su)", action: #selector(onSelectDev), keyEquivalent: "2")
        devItem.target = self
        envMenuItem.submenu = envMenu
        mainMenu.addItem(envMenuItem)
        
        // History Menu
        let historyMenuItem = NSMenuItem()
        let historyMenu = NSMenu(title: "История")
        historyMenu.addItem(withTitle: "Назад", action: #selector(onGoBack), keyEquivalent: "[")
        historyMenu.addItem(withTitle: "Вперед", action: #selector(onGoForward), keyEquivalent: "]")
        historyMenuItem.submenu = historyMenu
        mainMenu.addItem(historyMenuItem)
        
        // Window Menu
        let windowMenuItem = NSMenuItem()
        let windowMenu = NSMenu(title: "Окно")
        windowMenu.addItem(withTitle: "Убрать в Dock", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "Масштабировать", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")
        windowMenu.addItem(NSMenuItem.separator())
        windowMenu.addItem(withTitle: "Объединить все окна", action: #selector(NSWindow.mergeAllWindows(_:)), keyEquivalent: "")
        windowMenuItem.submenu = windowMenu
        mainMenu.addItem(windowMenuItem)
        
        NSApp.mainMenu = mainMenu
        NSApp.windowsMenu = windowMenu
    }
    
    private var currentActiveController: MainWindowController? {
        guard let keyWindow = NSApp.keyWindow else { return windowControllers.first }
        return windowControllers.first(where: { $0.window === keyWindow }) ?? windowControllers.first
    }
    
    @objc private func onNewWindow() {
        let controller = MainWindowController()
        addWindowController(controller)
        controller.showWindow(nil)
        controller.window?.center()
    }
    
    @objc private func onNewTab() {
        let controller = MainWindowController()
        addWindowController(controller)
        if let keyWindow = NSApp.keyWindow, let newWindow = controller.window {
            keyWindow.addTabbedWindow(newWindow, ordered: .above)
            newWindow.makeKeyAndOrderFront(nil)
        } else {
            controller.showWindow(nil)
        }
    }
    
    func openTab(with request: URLRequest, relativeTo parentWindow: NSWindow?) {
        let controller = MainWindowController(initialRequest: request)
        addWindowController(controller)
        if let hostWindow = parentWindow ?? NSApp.keyWindow, let newWindow = controller.window {
            hostWindow.addTabbedWindow(newWindow, ordered: .above)
            newWindow.makeKeyAndOrderFront(nil)
        } else {
            controller.showWindow(nil)
        }
    }
    
    @objc private func onCloseTabOrWindow() {
        if let keyWindow = NSApp.keyWindow {
            keyWindow.performClose(nil)
        }
    }
    
    @objc private func onPrint() { currentActiveController?.printCurrentPage() }
    @objc private func onReload() { currentActiveController?.reload() }
    @objc private func onHardReload() { currentActiveController?.reloadIgnoringCache() }
    @objc private func onZoomIn() { currentActiveController?.zoomIn() }
    @objc private func onZoomOut() { currentActiveController?.zoomOut() }
    @objc private func onResetZoom() { currentActiveController?.resetZoom() }
    @objc private func onGoBack() { currentActiveController?.goBack() }
    @objc private func onGoForward() { currentActiveController?.goForward() }
    @objc private func onSelectProd() { currentActiveController?.navigateToEnvironment(.production) }
    @objc private func onSelectDev() { currentActiveController?.navigateToEnvironment(.dev) }
    @objc private func onShowInspector() { currentActiveController?.showInspector() }
    @objc private func onShowConsole() { currentActiveController?.showConsole() }
}

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.run()
}
