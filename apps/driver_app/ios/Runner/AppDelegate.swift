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
    // lit pas tout seul. SANS CET APPEL L'APPLICATION MEURT — elle ne se
    // contente pas d'afficher une carte grise, comme l'affirmait ce
    // commentaire : GMSServices leve une NSException et le processus s'arrete
    // a la premiere vue de carte.
    //
    // LE NOM DE LA VARIABLE EST UN PIEGE DEJA TOMBE UNE FOIS : Info.plist lit
    // « $(GOOGLE_MAPS_API_KEY) », et un Secrets.xcconfig qui declarait
    // « MAPS_API_KEY » laissait donc GMSApiKey vide, sans que rien ne s'en
    // plaigne avant le plantage.
    if let cle = Bundle.main.object(forInfoDictionaryKey: "GMSApiKey") as? String,
       !cle.isEmpty {
      GMSServices.provideAPIKey(cle)
    } else {
      // ON TOMBE ICI TOUT DE SUITE, PAS QUARANTE IMAGES PLUS TARD.
      //
      // L'ancienne version se contentait de journaliser et continuait. Le SDK
      // Google Maps, lui, leve « GMSServicesException » a la PREMIERE carte —
      // c'est-a-dire depuis la fabrique de vues de plateforme, au fond d'une
      // pile C++ de quarante etages ou rien ne mentionne ni xcconfig, ni cle.
      // Le message utile existait, mais il etait deja sorti de l'ecran.
      //
      // MOURIR ICI EST PLUS AIMABLE : le message est la premiere chose que
      // voit celui qui lance l'application, et il dit quoi faire.
      NSLog("HBA : aucune cle Google Maps. Copiez ios/Flutter/Secrets.xcconfig.example en Secrets.xcconfig et renseignez GOOGLE_MAPS_API_KEY.")

      #if DEBUG
        // EN DEBOGAGE SEULEMENT. En release, l'application se lance quand
        // meme : elle mourra a la premiere carte, ce qui est deja le cas
        // aujourd'hui, mais ajouter de mon propre chef un plantage au
        // lancement d'une version publiee serait un choix qui ne m'appartient
        // pas.
        fatalError(
          "Aucune cle Google Maps. Copiez ios/Flutter/Secrets.xcconfig.example "
            + "en ios/Flutter/Secrets.xcconfig et renseignez "
            + "GOOGLE_MAPS_API_KEY. La variable doit porter EXACTEMENT ce nom : "
            + "Info.plist lit $(GOOGLE_MAPS_API_KEY)."
        )
      #endif
    }

    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
  }
}
