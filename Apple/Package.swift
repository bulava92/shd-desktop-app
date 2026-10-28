// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "SHD",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .executable(name: "SHD", targets: ["SHD"])
    ],
    targets: [
        .executableTarget(
            name: "SHD",
            path: "Sources/SHD"
        )
    ]
)
