import Flutter
import GoogleMaps
import UIKit

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    // LA CLE EST LUE DEPUIS Info.plist, PAS ECRITE ICI. Elle y est substituee
    // au build depuis Flutter/Secrets.xcconfig, qui n'est pas versionne.
    //
    // Sans appel a provideAPIKey, la carte s'affiche grise et le SDK n'ecrit
    // qu'un avertissement discret dans la console : le symptome ne designe pas
    // la cause. D'ou l'echec net ci-dessous en debug.
    let key = Bundle.main.object(forInfoDictionaryKey: "GMSApiKey") as? String ?? ""

    // TROIS FACONS D'ARRIVER ICI SANS VRAIE CLE, ET ELLES SE RESSEMBLENT TOUTES
    // A L'EXECUTION :
    //   - Secrets.xcconfig absent, donc valeur vide ;
    //   - le fichier copie mais laisse a REMPLACER ;
    //   - « $(MAPS_API_KEY) » recopie tel quel, faute de substitution au build.
    //
    // Le troisieme est le plus traitre : la chaine n'est ni vide ni egale au
    // gabarit, donc elle passerait pour une cle. provideAPIKey l'accepterait
    // sans broncher, et le SDK n'echouerait qu'a la creation de la premiere
    // carte, dans +[GMSServices checkServicePreconditions] — une pile qui ne
    // nomme ni la cle, ni le fichier, ni l'etape manquante.
    let looksConfigured = !key.isEmpty && key != "REMPLACER" && !key.contains("$(")

    if looksConfigured {
      GMSServices.provideAPIKey(key)
    } else {
      let diagnostic =
        "Cle Maps inutilisable (valeur lue : « \(key) »). "
        + "Copier ios/Flutter/Secrets.xcconfig.example en Secrets.xcconfig et y "
        + "renseigner MAPS_API_KEY, puis relancer un build complet — un hot "
        + "restart ne relit pas les xcconfig."

      #if DEBUG
      assertionFailure(diagnostic)
      #else
      NSLog("%@", diagnostic)
      #endif
    }

    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
  }
}
