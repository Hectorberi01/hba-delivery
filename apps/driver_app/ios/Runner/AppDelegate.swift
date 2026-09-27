import Flutter
import GoogleMaps
import UIKit

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    // LE SDK GOOGLE MAPS VEUT SA CLE AVANT LA PREMIERE CARTE, et il ne la
    // lit pas tout seul : sans cet appel, la vue reste grise sans erreur
    // visible cote Dart.
    if let cle = Bundle.main.object(forInfoDictionaryKey: "GMSApiKey") as? String,
       !cle.isEmpty {
      GMSServices.provideAPIKey(cle)
    } else {
      NSLog("HBA : aucune cle Google Maps. Copiez ios/Flutter/Secrets.xcconfig.example en Secrets.xcconfig et renseignez GOOGLE_MAPS_API_KEY.")
    }

    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
  }
}
