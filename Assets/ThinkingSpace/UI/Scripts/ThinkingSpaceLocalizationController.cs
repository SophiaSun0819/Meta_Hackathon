using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace ThinkingSpace.UI
{
    public readonly struct ThinkingSpaceStringEntry
    {
        public readonly string Key, English, Spanish, French;
        public ThinkingSpaceStringEntry(string key, string english, string spanish, string french)
        { Key=key; English=english; Spanish=spanish; French=french; }
    }

    public static class ThinkingSpaceStringCatalog
    {
        public const string TableName = "ThinkingSpace UI";
        public static readonly string[] SupportedLocaleCodes = { "en", "es", "fr", "zh-CN", "hi-IN" };
        public static readonly string[] PreparedLocaleCodes = Array.Empty<string>();

        public static readonly ThinkingSpaceStringEntry[] Entries =
        {
            new("app.brainstorm", "Brainstorm", "Ideas", "Idéation"),
            new("menu.sessions.title", "Brainstorming Sessions", "Sesiones de ideas", "Sessions d'idéation"),
            new("menu.sessions.empty", "No sessions yet", "Aún no hay sesiones", "Aucune session"),
            new("menu.session.start", "Start New Session", "Nueva sesión", "Nouvelle session"),
            new("menu.session.import", "Import Session", "Importar sesión", "Importer une session"),
            new("menu.session.export", "Export Session", "Exportar sesión", "Exporter la session"),
            new("menu.session.end", "End Session", "Finalizar sesión", "Terminer la session"),
            new("menu.session.format", "Session {0}", "Sesión {0}", "Session {0}"),
            new("menu.voice_commands", "Voice Commands", "Comandos de voz", "Commandes vocales"),
            new("menu.hand_gestures", "Hand Gestures", "Gestos de mano", "Gestes de la main"),
            new("menu.my_sessions", "My Sessions", "Mis sesiones", "Mes sessions"),
            new("menu.settings", "Settings", "Ajustes", "Paramètres"),
            new("menu.help", "Help", "Ayuda", "Aide"),
            new("menu.home", "Home", "Inicio", "Accueil"),
            new("menu.ready", "Ready", "Listo", "Prêt"),
            new("menu.start.description", "Begin a new spatial brainstorm", "Inicia una nueva lluvia de ideas espacial", "Commencez une nouvelle réflexion spatiale"),
            new("menu.sessions.description", "Open a saved thinking space", "Abre un espacio de ideas guardado", "Ouvrez un espace de réflexion enregistré"),
            new("menu.voice.description", "Review supported voice controls", "Consulta los controles de voz disponibles", "Consultez les commandes vocales disponibles"),
            new("menu.gestures.description", "Review hand interaction guidance", "Consulta la guía de interacción manual", "Consultez le guide des interactions manuelles"),
            new("menu.feature.unavailable", "Not available yet", "Aún no disponible", "Pas encore disponible"),
            new("menu.challenge", "Challenge My Idea", "Cuestionar mi idea", "Remettre mon idée en question"),
            new("menu.expand", "Expand My Idea", "Ampliar mi idea", "Développer mon idée"),
            new("menu.connect", "Connect My Ideas", "Conectar mis ideas", "Relier mes idées"),
            new("menu.whiteboard", "Spawn Whiteboard", "Crear pizarra", "Créer un tableau"),
            new("menu.help.voice", "Change the interface with commands such as ‘Switch the UI to Hindi’, ‘Show the interface in Spanish’, or ‘Change menu language to French’. English, Spanish, French, Simplified Chinese, and Hindi are supported.", "Cambia la interfaz con comandos como ‘Cambia la interfaz a hindi’, ‘Muestra la interfaz en español’ o ‘Cambia el idioma del menú a francés’. Se admiten inglés, español, francés, chino simplificado e hindi.", "Changez l’interface avec des commandes comme « Passe l’interface en hindi », « Affiche l’interface en espagnol » ou « Change la langue du menu en français ». L’anglais, l’espagnol, le français, le chinois simplifié et l’hindi sont pris en charge."),
            new("menu.help.gestures", "Gesture reference will be expanded as interactions are finalized.", "La guía de gestos se ampliará al finalizar las interacciones.", "Le guide des gestes sera complété à mesure que les interactions seront finalisées."),
            new("pairing.account.title", "Account / Device Connection", "Cuenta / conexión del dispositivo", "Compte / connexion de l’appareil"),
            new("pairing.account.disconnected", "This headset is not paired. Pairing remains unavailable until the shared website service is finalized.", "Este visor no está vinculado. La vinculación no estará disponible hasta que finalice el servicio web compartido.", "Ce casque n’est pas associé. L’association restera indisponible jusqu’à la finalisation du service web partagé."),
            new("pairing.unavailable.title", "Pairing Unavailable", "Vinculación no disponible", "Association indisponible"),
            new("pairing.unavailable.body", "Pairing is not available yet or the network is offline. Your local sessions are safe.", "La vinculación aún no está disponible o no hay conexión. Tus sesiones locales están seguras.", "L’association n’est pas encore disponible ou le réseau est hors ligne. Vos sessions locales sont protégées."),
            new("pairing.requesting.title", "Requesting Pairing Code", "Solicitando código de vinculación", "Demande du code d’association"),
            new("pairing.requesting.body", "Contacting the pairing service. No microphone or session data is being changed.", "Contactando con el servicio de vinculación. No se cambiarán el micrófono ni los datos de la sesión.", "Connexion au service d’association. Le microphone et les données de session ne sont pas modifiés."),
            new("pairing.code.title", "Pair This Headset", "Vincular este visor", "Associer ce casque"),
            new("pairing.code.body", "Enter this six-digit code on the Thinking Space website.", "Introduce este código de seis dígitos en el sitio web de Thinking Space.", "Saisissez ce code à six chiffres sur le site Thinking Space."),
            new("pairing.waiting.title", "Waiting for Website", "Esperando al sitio web", "En attente du site web"),
            new("pairing.waiting.body", "Keep this page open while the website confirms the code.", "Mantén esta página abierta mientras el sitio web confirma el código.", "Gardez cette page ouverte pendant que le site confirme le code."),
            new("pairing.confirm.title", "Confirm Account on Quest", "Confirmar cuenta en Quest", "Confirmer le compte sur Quest"),
            new("pairing.confirm.body", "Pair this headset with {0}?", "¿Vincular este visor con {0}?", "Associer ce casque à {0} ?"),
            new("pairing.paired.title", "Paired Successfully", "Vinculación completada", "Association réussie"),
            new("pairing.paired.body", "This headset is paired. Cloud synchronization is not enabled in this milestone.", "Este visor está vinculado. La sincronización en la nube no está habilitada en este hito.", "Ce casque est associé. La synchronisation cloud n’est pas activée dans ce jalon."),
            new("pairing.expired.title", "Pairing Code Expired", "El código ha caducado", "Code d’association expiré"),
            new("pairing.expired.body", "Generate a new code when the pairing service is available.", "Genera un código nuevo cuando el servicio de vinculación esté disponible.", "Générez un nouveau code lorsque le service d’association sera disponible."),
            new("pairing.failed.title", "Connection Failed", "Error de conexión", "Échec de la connexion"),
            new("pairing.failed.body", "The headset could not finish pairing. Your local sessions were not changed.", "El visor no pudo completar la vinculación. Tus sesiones locales no se modificaron.", "Le casque n’a pas pu terminer l’association. Vos sessions locales n’ont pas été modifiées."),
            new("pairing.unpair.title", "Unpair Headset", "Desvincular visor", "Dissocier le casque"),
            new("pairing.unpair.body", "Unpair this headset? Local sessions will remain on this device.", "¿Desvincular este visor? Las sesiones locales permanecerán en el dispositivo.", "Dissocier ce casque ? Les sessions locales resteront sur cet appareil."),
            new("pairing.expires", "Expires in {0} seconds", "Caduca en {0} segundos", "Expire dans {0} secondes"),
            new("pairing.action.pair", "Pair This Headset", "Vincular este visor", "Associer ce casque"),
            new("pairing.action.new_code", "Generate New Code", "Generar código nuevo", "Générer un nouveau code"),
            new("pairing.action.confirm", "Confirm Account", "Confirmar cuenta", "Confirmer le compte"),
            new("pairing.action.unpair", "Unpair", "Desvincular", "Dissocier"),
            new("pairing.action.done", "Done", "Listo", "Terminé"),
            new("common.close_menu", "Close Menu", "Cerrar menú", "Fermer le menu"),
            new("common.back", "Back", "Atrás", "Retour"),
            new("common.previous", "Previous", "Anterior", "Précédent"),
            new("common.next", "Next", "Siguiente", "Suivant"),
            new("common.page_format", "Page {0} / {1}", "Página {0} / {1}", "Page {0} / {1}"),
            new("common.delete", "Delete", "Eliminar", "Supprimer"),
            new("common.cancel", "Cancel", "Cancelar", "Annuler"),
            new("common.close", "Close", "Cerrar", "Fermer"),
            new("common.yes", "Yes", "Sí", "Oui"),
            new("common.no", "No", "No", "Non"),
            new("common.retry", "Retry", "Reintentar", "Réessayer"),
            new("common.confirm", "Confirm", "Confirmar", "Confirmer"),
            new("common.menu", "Menu", "Menú", "Menu"),
            new("menu.recenter", "Recenter", "Volver a centrar", "Recentrer"),
            new("settings.recording_hand", "Recording Hand", "Mano de grabación", "Main d'enregistrement"),
            new("settings.left", "Left", "Izquierda", "Gauche"),
            new("settings.right", "Right", "Derecha", "Droite"),
            new("settings.recording.title", "Recording Settings", "Ajustes de grabación", "Paramètres d'enregistrement"),
            new("settings.conversational", "Conversational", "Conversacional", "Conversationnel"),
            new("settings.manual", "Manual", "Manual", "Manuel"),
            new("settings.subtitles_on", "Subtitles On", "Subtítulos activados", "Sous-titres activés"),
            new("settings.subtitles_off", "Subtitles Off", "Subtítulos desactivados", "Sous-titres désactivés"),
            new("settings.edit_name", "Edit Name", "Editar nombre", "Modifier le nom"),
            new("settings.replay_tutorial", "Replay Tutorial", "Repetir tutorial", "Revoir le tutoriel"),
            new("settings.wrist.title", "Wrist Menu", "Menú de muñeca", "Menu au poignet"),
            new("settings.wrist.left", "Watch: Left", "Reloj: izquierda", "Montre : gauche"),
            new("settings.wrist.right", "Watch: Right", "Reloj: derecha", "Montre : droite"),
            new("settings.interface_language", "Interface Language", "Idioma de la interfaz", "Langue de l'interface"),
            new("language.english", "English", "Inglés", "Anglais"),
            new("language.spanish", "Spanish", "Español", "Espagnol"),
            new("language.french", "French", "Francés", "Français"),
            new("language.mandarin", "Mandarin", "Mandarín", "Mandarin"),
            new("language.hindi", "Hindi", "Hindi", "Hindi"),
            new("settings.language.title", "Language Settings", "Ajustes de idioma", "Paramètres de langue"),
            new("settings.conversation_mode", "Conversation Language", "Idioma de conversación", "Langue de conversation"),
            new("settings.conversation.unavailable", "Explicit languages unavailable — backend compatibility mode", "Idiomas explícitos no disponibles — modo de compatibilidad del servidor", "Langues explicites indisponibles — mode de compatibilité du serveur"),
            new("settings.conversation.active", "Active: {0}", "Activo: {0}", "Actif : {0}"),
            new("settings.language.auto", "Automatic", "Automático", "Automatique"),
            new("settings.language.specific", "Specific Language", "Idioma específico", "Langue spécifique"),
            new("settings.voice_profile", "Orby Voice", "Voz de Orby", "Voix d'Orby"),
            new("settings.voice.auto", "Automatic Voice", "Voz automática", "Voix automatique"),
            new("settings.voice_language.info", "Voice, subtitle and Orby voice language are controlled separately by conversation settings and service capability. Interface language does not change them.", "El idioma de voz, subtítulos y voz de Orby se controla por separado mediante los ajustes de conversación y la capacidad del servicio. El idioma de la interfaz no lo cambia.", "La langue de la voix, des sous-titres et de la voix d’Orby est contrôlée séparément par les réglages de conversation et les capacités du service. La langue de l’interface ne la modifie pas."),
            new("language.confirm.title", "Change Interface Language?", "¿Cambiar el idioma de la interfaz?", "Changer la langue de l'interface ?"),
            new("language.confirm.body", "Change the interface to {0}?", "¿Cambiar la interfaz a {0}?", "Changer l'interface en {0} ?"),
            new("language.unsupported_ui", "That language is available for conversation, but its interface translation is not ready.", "Ese idioma está disponible para conversar, pero la interfaz aún no está traducida.", "Cette langue est disponible pour la conversation, mais son interface n'est pas encore traduite."),
            new("language.command.title", "Interface Language", "Idioma de la interfaz", "Langue de l'interface"),
            new("language.command.success", "Interface changed to {0}.", "La interfaz cambió a {0}.", "L'interface est maintenant en {0}."),
            new("language.command.failed", "I couldn't change the interface language. Your other language settings were not changed.", "No pude cambiar el idioma de la interfaz. Los demás ajustes de idioma no cambiaron.", "Je n'ai pas pu changer la langue de l'interface. Les autres paramètres de langue n'ont pas changé."),
            new("language.command.unsupported", "That interface language isn't available yet. English, Spanish, French, Simplified Chinese and Hindi are supported.", "Ese idioma de interfaz aún no está disponible. Se admiten inglés, español, francés, chino simplificado e hindi.", "Cette langue d'interface n'est pas encore disponible. L'anglais, l'espagnol, le français, le chinois simplifié et l'hindi sont pris en charge."),
            new("language.command.ambiguous", "Please name one interface language: English, Spanish, French, Simplified Chinese or Hindi.", "Indica un solo idioma de interfaz: inglés, español, francés, chino simplificado o hindi.", "Veuillez choisir une seule langue d'interface : anglais, espagnol, français, chinois simplifié ou hindi."),
            new("recording.complete", "Recording Complete", "Grabación completada", "Enregistrement terminé"),
            new("recording.play", "Play Recording", "Reproducir grabación", "Lire l'enregistrement"),
            new("recording.stop", "Stop Playback", "Detener reproducción", "Arrêter la lecture"),
            new("recording.discard", "Discard", "Descartar", "Supprimer"),
            new("recording.rerecord", "Rerecord", "Volver a grabar", "Réenregistrer"),
            new("recording.send", "Send to AI", "Enviar a la IA", "Envoyer à l'IA"),
            new("recording.send_orby", "Send to Orby", "Enviar a Orby", "Envoyer à Orby"),
            new("transcript.transcribing", "Transcribing...", "Transcribiendo...", "Transcription..."),
            new("transcript.complete", "Transcription Complete", "Transcripción completada", "Transcription terminée"),
            new("transcript.failed", "Transcription Failed", "Error de transcripción", "Échec de la transcription"),
            new("transcript.back", "Back to Recording", "Volver a la grabación", "Retour à l'enregistrement"),
            new("idea.play_summary", "Play Summary", "Reproducir resumen", "Lire le résumé"),
            new("idea.stop_summary", "Stop Summary", "Detener resumen", "Arrêter le résumé"),
            new("idea.return", "Return", "Devolver", "Renvoyer"),
            new("idea.delete", "Delete Idea", "Eliminar idea", "Supprimer l'idée"),
            new("idea.confirm_delete", "Confirm Delete", "Confirmar eliminación", "Confirmer la suppression"),
            new("idea.delete_question", "Delete Idea?", "¿Eliminar la idea?", "Supprimer l'idée ?"),
            new("idea.details", "Idea Details", "Detalles de la idea", "Détails de l'idée"),
            new("idea.summary", "Summary", "Resumen", "Résumé"),
            new("idea.category", "Category", "Categoría", "Catégorie"),
            new("idea.color", "Color", "Color", "Couleur"),
            new("idea.color_title", "Bubble Color · {0}", "Color de burbuja · {0}", "Couleur de bulle · {0}"),
            new("idea.delete_body", "Delete “{0}”? References to it will be removed. Derived ideas are preserved.", "¿Eliminar «{0}»? Se quitarán sus referencias. Las ideas derivadas se conservarán.", "Supprimer « {0} » ? Ses références seront retirées. Les idées dérivées seront conservées."),
            new("idea.delete_failed", "Delete Failed", "Error al eliminar", "Échec de la suppression"),
            new("idea.color_failed", "Color Change Failed", "Error al cambiar el color", "Échec du changement de couleur"),
            new("recording.total", "Total  {0}", "Total  {0}", "Total  {0}"),
            new("recording.elapsed", "{0} elapsed     -{1} remaining", "{0} transcurrido     -{1} restante", "{0} écoulé     -{1} restant"),
            new("ai.response", "AI Response", "Respuesta de IA", "Réponse de l'IA"),
            new("ai.clarification", "Orby Needs Clarification", "Orby necesita una aclaración", "Orby a besoin d'une précision"),
            new("ai.record_answer", "Record Answer", "Grabar respuesta", "Enregistrer la réponse"),
            new("state.idle", "Idle", "En espera", "En attente"),
            new("state.listening", "Listening", "Escuchando", "À l'écoute"),
            new("state.thinking", "Thinking", "Pensando", "Réflexion"),
            new("state.speaking", "Speaking", "Hablando", "Parle"),
            new("state.reviewing", "Reviewing", "Revisando", "Révision"),
            new("state.error", "Error", "Error", "Erreur"),
            new("hud.mic_on", "Mic On", "Micrófono activado", "Micro activé"),
            new("hud.mic_off", "Mic Off", "Micrófono desactivado", "Micro désactivé"),
            new("hud.mic_off_finishing", "Mic Off · Finishing current thought", "Micrófono desactivado · Terminando la idea actual", "Micro désactivé · Fin de la pensée en cours"),
            new("hud.mic_idle", "Mic On · Idle", "Micrófono · En espera", "Micro · En attente"),
            new("hud.speech_detected", "Speech Detected", "Voz detectada", "Parole détectée"),
            new("hud.finishing", "Finishing Thought", "Terminando idea", "Fin de la pensée"),
            new("state.paused", "Paused", "En pausa", "En pause"),
            new("orby.status", "Orby · {0}", "Orby · {0}", "Orby · {0}"),
            new("orby.finishing", "Orby · Finishing thought…", "Orby · Terminando idea…", "Orby · Termine sa réflexion…"),
            new("subtitle.timing", "Approximate timing · {0}/{1}", "Tiempo aproximado · {0}/{1}", "Durée approximative · {0}/{1}"),
            new("ai.request_failed", "AI Request Failed", "Error en la solicitud de IA", "Échec de la requête IA"),
            new("ai.clarification_ended", "Clarification Ended", "Aclaración finalizada", "Clarification terminée"),
            new("ai.invalid_response", "Invalid AI Response", "Respuesta de IA no válida", "Réponse IA non valide"),
            new("ai.idea_failed", "Idea Processing Failed", "Error al procesar la idea", "Échec du traitement de l'idée"),
            new("ai.idea_save_failed", "Idea Save Failed", "Error al guardar la idea", "Échec de l'enregistrement de l'idée"),
            new("ai.visual_unavailable", "Idea Visual Unavailable", "Visual de idea no disponible", "Visuel de l'idée indisponible"),
            new("ai.voice_unavailable", "Voice Unavailable", "Voz no disponible", "Voix indisponible"),
            new("error.recoverable_title", "Orby couldn't finish", "Orby no pudo terminar", "Orby n'a pas pu terminer"),
            new("error.network_unavailable", "I couldn't connect. Please try again.", "No pude conectarme. Inténtalo de nuevo.", "Je n'ai pas pu me connecter. Réessayez."),
            new("error.ai_processing_failed", "I couldn't finish that request.", "No pude terminar esa solicitud.", "Je n'ai pas pu terminer cette demande."),
            new("error.speech_not_understood", "I didn't catch that. Please try speaking again.", "No entendí eso. Intenta hablar de nuevo.", "Je n'ai pas compris. Essayez de parler à nouveau."),
            new("error.unsupported_capability", "I can't generate that yet, but I can help you plan it.", "Todavía no puedo generar eso, pero puedo ayudarte a planificarlo.", "Je ne peux pas encore générer cela, mais je peux vous aider à le planifier."),
            new("error.generation_failed", "The visual couldn't be completed. You can retry.", "No se pudo completar el elemento visual. Puedes intentarlo de nuevo.", "Le visuel n'a pas pu être terminé. Vous pouvez réessayer."),
            new("error.session_safe", "Your session and recording are safe.", "Tu sesión y tu grabación están seguras.", "Votre session et votre enregistrement sont conservés."),
            new("common.dismiss", "Dismiss", "Cerrar", "Masquer"),
            new("onboarding.name_prompt", "What should Orby call you?", "¿Cómo quieres que te llame Orby?", "Comment Orby doit-elle vous appeler ?"),
            new("onboarding.skip_name", "Skip Name", "Omitir nombre", "Ignorer le nom"),
            new("onboarding.skip_tutorial", "Skip Tutorial", "Omitir tutorial", "Ignorer le tutoriel"),
            new("onboarding.space", "Space", "Espacio", "Espace"),
            new("gesture.hold_fist", "Hold Fist", "Mantén el puño", "Maintenez le poing"),
            new("recording.active", "● Recording", "● Grabando", "● Enregistrement"),
            new("color.category", "Category", "Categoría", "Catégorie"),
            new("color.blue", "Blue", "Azul", "Bleu"),
            new("color.cyan", "Cyan", "Cian", "Cyan"),
            new("color.green", "Green", "Verde", "Vert"),
            new("color.yellow", "Yellow", "Amarillo", "Jaune"),
            new("color.orange", "Orange", "Naranja", "Orange"),
            new("color.red", "Red", "Rojo", "Rouge"),
            new("color.pink", "Pink", "Rosa", "Rose"),
            new("color.purple", "Purple", "Morado", "Violet"),
            new("tutorial.open.title", "Open the menu", "Abrir el menú", "Ouvrir le menu"),
            new("tutorial.open.body", "Close a fist, then open the same palm upward.", "Cierra el puño y luego abre la misma palma hacia arriba.", "Fermez le poing, puis ouvrez la même paume vers le haut."),
            new("tutorial.session.title", "Start a session", "Iniciar una sesión", "Démarrer une session"),
            new("tutorial.session.body", "Choose Start New Session before recording ideas.", "Elige Nueva sesión antes de grabar ideas.", "Choisissez Nouvelle session avant d'enregistrer des idées."),
            new("tutorial.listen.title", "Start listening", "Activar la escucha", "Activer l'écoute"),
            new("tutorial.listen.body", "Hold a thumbs-up with the selected recording hand.", "Mantén el pulgar hacia arriba con la mano de grabación.", "Maintenez le pouce levé avec la main d'enregistrement."),
            new("tutorial.stop.title", "Stop listening", "Detener la escucha", "Arrêter l'écoute"),
            new("tutorial.stop.body", "Hold a thumbs-down to finish the recording.", "Mantén el pulgar hacia abajo para terminar la grabación.", "Maintenez le pouce baissé pour terminer l'enregistrement."),
            new("tutorial.ideas.title", "Explore ideas", "Explorar ideas", "Explorer les idées"),
            new("tutorial.ideas.body", "Use Meta pinch and poke interactions to work with idea bubbles.", "Usa los gestos de pinza y toque de Meta para interactuar con las burbujas.", "Utilisez les interactions de pincement et de toucher Meta pour manipuler les bulles."),
            new("settings.seconds_format", "{0} seconds", "{0} segundos", "{0} secondes"),
            new("settings.decrease_silence", "− 0.5s", "− 0,5 s", "− 0,5 s"),
            new("settings.increase_silence", "+ 0.5s", "+ 0,5 s", "+ 0,5 s"),
            new("session.delete.title", "Delete Session {0}?", "¿Eliminar la sesión {0}?", "Supprimer la session {0} ?"),
            new("session.delete.body", "Its ideas and conversation history will be removed.", "Se eliminarán sus ideas y el historial de conversación.", "Ses idées et son historique de conversation seront supprimés."),
            new("session.delete.confirm", "Delete Session", "Eliminar sesión", "Supprimer la session"),
            new("transfer.busy", "Another document operation is already open.", "Ya hay otra operación de documento abierta.", "Une autre opération de document est déjà ouverte."),
            new("transfer.choose_import", "Choose a Thinking Space JSON session to import.", "Elige una sesión JSON de Thinking Space para importar.", "Choisissez une session JSON Thinking Space à importer."),
            new("transfer.picker_unavailable", "Quest document picker is unavailable on this device; no file was accessed.", "El selector de documentos de Quest no está disponible; no se accedió a ningún archivo.", "Le sélecteur de documents Quest est indisponible ; aucun fichier n'a été consulté."),
            new("transfer.picker_failed", "Quest document picker could not start; no file was accessed.", "No se pudo iniciar el selector de documentos de Quest; no se accedió a ningún archivo.", "Le sélecteur de documents Quest n'a pas pu démarrer ; aucun fichier n'a été consulté."),
            new("transfer.invalid_result", "Document picker returned an invalid result.", "El selector de documentos devolvió un resultado no válido.", "Le sélecteur de documents a renvoyé un résultat non valide."),
            new("transfer.stage_failed", "Selected import could not be staged safely.", "La importación seleccionada no se pudo preparar de forma segura.", "L'importation sélectionnée n'a pas pu être préparée en toute sécurité."),
            new("transfer.choose_export", "Choose where to save {0}.", "Elige dónde guardar {0}.", "Choisissez où enregistrer {0}."),
            new("transfer.export_unavailable", "Quest document picker is unavailable. The app-owned export remains at:\n{0}", "El selector de documentos de Quest no está disponible. La exportación de la aplicación permanece en:\n{0}", "Le sélecteur de documents Quest est indisponible. L'exportation de l'application reste dans :\n{0}"),
            new("transfer.export_picker_failed", "Quest document picker could not start. The app-owned export was preserved.", "No se pudo iniciar el selector de documentos de Quest. Se conservó la exportación de la aplicación.", "Le sélecteur de documents Quest n'a pas pu démarrer. L'exportation de l'application a été conservée."),
            new("error.session_delete", "Session deletion failed.", "No se pudo eliminar la sesión.", "La suppression de la session a échoué."),
            new("error.transfer_busy", "Another session transfer is already running.", "Ya hay otra transferencia de sesión en curso.", "Un autre transfert de session est déjà en cours.")
        };

        private static readonly Dictionary<string,ThinkingSpaceStringEntry> ByKey = BuildByKey();
        private static readonly Dictionary<string,string> KeyByEnglish = BuildByEnglish();
        private static readonly Dictionary<string,string> Chinese = new(StringComparer.Ordinal)
        {
            ["app.brainstorm"]="头脑风暴", ["menu.sessions.title"]="头脑风暴会话",
            ["menu.sessions.empty"]="暂无会话", ["menu.session.start"]="开始新会话",
            ["menu.session.import"]="导入会话", ["menu.session.export"]="导出会话",
            ["menu.session.end"]="结束会话", ["menu.session.format"]="会话 {0}",
            ["menu.my_sessions"]="我的会话", ["menu.settings"]="设置", ["menu.help"]="帮助",
            ["menu.home"]="主页", ["menu.ready"]="就绪", ["menu.recenter"]="重新居中",
            ["menu.start.description"]="开始新的空间头脑风暴",
            ["menu.sessions.description"]="打开已保存的思维空间",
            ["menu.voice_commands"]="语音命令", ["menu.hand_gestures"]="手势",
            ["menu.voice.description"]="查看支持的语音控制",
            ["menu.gestures.description"]="查看手势交互指南",
            ["menu.help.voice"]="可使用“将界面切换为印地语”“用西班牙语显示界面”或“将菜单语言改为法语”等命令更改界面。支持英语、西班牙语、法语、简体中文和印地语。",
            ["menu.help.gestures"]="捏合或指向按钮，并从专用抓取区域移动面板。",
            ["menu.feature.unavailable"]="尚不可用",
            ["pairing.account.title"]="账户 / 设备连接",
            ["pairing.account.disconnected"]="此头显尚未配对。共享网站服务完成前，配对保持不可用。",
            ["pairing.unavailable.title"]="配对不可用",
            ["pairing.unavailable.body"]="配对尚不可用或网络离线。您的本地会话是安全的。",
            ["pairing.code.title"]="配对此头显", ["pairing.code.body"]="在 Thinking Space 网站输入此六位代码。",
            ["pairing.waiting.title"]="等待网站确认", ["pairing.waiting.body"]="网站确认代码时请保持此页面打开。",
            ["pairing.confirm.title"]="在 Quest 上确认账户", ["pairing.confirm.body"]="将此头显与 {0} 配对？",
            ["pairing.paired.title"]="配对成功", ["pairing.paired.body"]="头显已配对。此版本尚未启用云同步。",
            ["pairing.expired.title"]="配对代码已过期", ["pairing.failed.title"]="连接失败",
            ["pairing.action.pair"]="配对此头显", ["pairing.action.new_code"]="生成新代码",
            ["pairing.action.confirm"]="确认账户", ["pairing.action.unpair"]="取消配对", ["pairing.action.done"]="完成",
            ["common.close_menu"]="关闭菜单", ["common.close"]="关闭", ["common.back"]="返回",
            ["common.previous"]="上一页", ["common.next"]="下一页", ["common.page_format"]="第 {0} / {1} 页",
            ["common.delete"]="删除", ["common.cancel"]="取消", ["common.confirm"]="确认",
            ["common.yes"]="是", ["common.no"]="否", ["common.retry"]="重试", ["common.menu"]="菜单",
            ["settings.recording.title"]="录音设置", ["settings.recording_hand"]="录音手",
            ["settings.left"]="左手", ["settings.right"]="右手", ["settings.conversational"]="对话模式",
            ["settings.manual"]="手动模式", ["settings.subtitles_on"]="字幕开启",
            ["settings.subtitles_off"]="字幕关闭", ["settings.edit_name"]="编辑姓名",
            ["settings.replay_tutorial"]="重播教程", ["settings.wrist.title"]="腕部菜单",
            ["settings.wrist.left"]="腕表：左手", ["settings.wrist.right"]="腕表：右手",
            ["settings.interface_language"]="界面语言", ["settings.language.title"]="语言设置",
            ["settings.conversation_mode"]="对话语言", ["settings.language.auto"]="自动",
            ["settings.conversation.active"]="语音语言由自动检测和服务能力决定。",
            ["settings.decrease_silence"]="缩短静音等待", ["settings.increase_silence"]="延长静音等待",
            ["settings.seconds_format"]="{0:0.0} 秒",
            ["settings.language.specific"]="指定语言", ["settings.voice_profile"]="Orby 语音",
            ["settings.voice.auto"]="自动语音", ["language.english"]="英语", ["language.spanish"]="西班牙语",
            ["language.french"]="法语", ["language.mandarin"]="简体中文", ["language.hindi"]="印地语",
            ["language.confirm.body"]="将界面语言更改为{0}？这不会更改对话语言或 Orby 的声音。",
            ["settings.voice_language.info"]="语音、字幕和 Orby 声音的语言由对话设置和服务能力单独决定。界面语言不会更改它们。",
            ["pairing.expires"]="代码将在 {0} 后过期",
            ["session.delete.title"]="删除会话", ["session.delete.confirm"]="删除会话",
            ["session.delete.body"]="删除“{0}”？此操作只会删除这个本地会话。",
            ["recording.complete"]="录音完成", ["recording.play"]="播放录音", ["recording.stop"]="停止播放",
            ["recording.discard"]="丢弃", ["recording.rerecord"]="重新录音", ["recording.send_orby"]="发送给 Orby",
            ["recording.total"]="总时长  {0}", ["recording.elapsed"]="已播放 {0}     剩余 {1}",
            ["transcript.transcribing"]="正在转写…", ["transcript.complete"]="转写完成",
            ["transcript.failed"]="转写失败", ["transcript.back"]="返回录音",
            ["idea.details"]="想法详情", ["idea.summary"]="摘要", ["idea.category"]="类别",
            ["idea.play_summary"]="播放摘要", ["idea.stop_summary"]="停止摘要", ["idea.return"]="返回原位",
            ["idea.delete"]="删除想法", ["idea.delete_question"]="删除想法？", ["idea.color"]="颜色",
            ["ai.response"]="AI 回复", ["ai.clarification"]="Orby 需要确认", ["ai.record_answer"]="录制回答",
            ["state.idle"]="空闲", ["state.listening"]="正在聆听", ["state.thinking"]="正在思考",
            ["state.speaking"]="正在说话", ["state.reviewing"]="正在查看", ["state.error"]="错误",
            ["state.paused"]="已暂停", ["hud.mic_on"]="麦克风开启", ["hud.mic_off"]="麦克风关闭",
            ["hud.mic_off_finishing"]="麦克风关闭 · 正在完成当前想法", ["hud.mic_idle"]="麦克风开启 · 空闲",
            ["hud.speech_detected"]="检测到语音", ["hud.finishing"]="正在结束发言",
            ["orby.status"]="Orby · {0}", ["orby.finishing"]="Orby · 正在完成想法…",
            ["ai.request_failed"]="AI 请求失败", ["ai.clarification_ended"]="澄清已结束",
            ["ai.invalid_response"]="AI 回复无效", ["ai.idea_failed"]="想法处理失败",
            ["ai.idea_save_failed"]="想法保存失败", ["ai.visual_unavailable"]="想法视觉内容不可用",
            ["ai.voice_unavailable"]="语音不可用",
            ["onboarding.name_prompt"]="Orby 应该怎么称呼你？", ["onboarding.skip_name"]="跳过姓名",
            ["onboarding.skip_tutorial"]="跳过教程", ["onboarding.space"]="空格",
            ["gesture.hold_fist"]="保持握拳", ["recording.active"]="● 正在录音",
            ["color.category"]="类别", ["color.blue"]="蓝色", ["color.cyan"]="青色",
            ["color.green"]="绿色", ["color.yellow"]="黄色", ["color.orange"]="橙色",
            ["color.red"]="红色", ["color.pink"]="粉色", ["color.purple"]="紫色"
        };
        private static readonly Dictionary<string,string> Hindi = new(StringComparer.Ordinal)
        {
            ["app.brainstorm"]="विचार-मंथन", ["menu.sessions.title"]="विचार-मंथन सत्र",
            ["menu.sessions.empty"]="अभी कोई सत्र नहीं", ["menu.session.start"]="नया सत्र शुरू करें",
            ["menu.session.import"]="सत्र आयात करें", ["menu.session.export"]="सत्र निर्यात करें",
            ["menu.session.end"]="सत्र समाप्त करें", ["menu.session.format"]="सत्र {0}",
            ["menu.my_sessions"]="मेरे सत्र", ["menu.settings"]="सेटिंग्स", ["menu.help"]="सहायता",
            ["menu.home"]="होम", ["menu.ready"]="तैयार", ["menu.recenter"]="फिर केंद्रित करें",
            ["menu.start.description"]="नया स्थानिक विचार-मंथन शुरू करें",
            ["menu.sessions.description"]="सहेजा हुआ थिंकिंग स्पेस खोलें",
            ["menu.voice_commands"]="वॉइस कमांड", ["menu.hand_gestures"]="हाथ के संकेत",
            ["menu.voice.description"]="समर्थित वॉइस नियंत्रण देखें",
            ["menu.gestures.description"]="हाथ से इंटरैक्शन की जानकारी देखें",
            ["menu.help.voice"]="‘यूआई को हिन्दी में बदलें’, ‘इंटरफ़ेस स्पेनिश में दिखाएँ’ या ‘मेनू भाषा फ़्रेंच करें’ जैसे कमांड से इंटरफ़ेस बदलें। अंग्रेज़ी, स्पेनिश, फ़्रेंच, सरलीकृत चीनी और हिन्दी समर्थित हैं।",
            ["menu.help.gestures"]="बटन के लिए पिंच या पॉइंट करें और पैनल को उसके ग्रैब क्षेत्र से खींचें।",
            ["menu.feature.unavailable"]="अभी उपलब्ध नहीं",
            ["pairing.account.title"]="खाता / डिवाइस कनेक्शन",
            ["pairing.account.disconnected"]="यह हेडसेट पेयर नहीं है। साझा वेबसाइट सेवा तैयार होने तक पेयरिंग उपलब्ध नहीं है।",
            ["pairing.unavailable.title"]="पेयरिंग उपलब्ध नहीं",
            ["pairing.unavailable.body"]="पेयरिंग अभी उपलब्ध नहीं है या नेटवर्क ऑफ़लाइन है। आपके स्थानीय सत्र सुरक्षित हैं।",
            ["pairing.code.title"]="इस हेडसेट को पेयर करें",
            ["pairing.code.body"]="Thinking Space वेबसाइट पर यह छह अंकों का कोड दर्ज करें।",
            ["pairing.waiting.title"]="वेबसाइट की पुष्टि की प्रतीक्षा",
            ["pairing.waiting.body"]="वेबसाइट कोड की पुष्टि करे तब तक यह पृष्ठ खुला रखें।",
            ["pairing.confirm.title"]="Quest पर खाता पुष्टि करें", ["pairing.confirm.body"]="इस हेडसेट को {0} से पेयर करें?",
            ["pairing.paired.title"]="पेयरिंग सफल", ["pairing.paired.body"]="हेडसेट पेयर है। इस संस्करण में क्लाउड सिंक चालू नहीं है।",
            ["pairing.expired.title"]="पेयरिंग कोड समाप्त", ["pairing.failed.title"]="कनेक्शन विफल",
            ["pairing.action.pair"]="इस हेडसेट को पेयर करें", ["pairing.action.new_code"]="नया कोड बनाएँ",
            ["pairing.action.confirm"]="खाता पुष्टि करें", ["pairing.action.unpair"]="अनपेयर", ["pairing.action.done"]="पूर्ण",
            ["common.close_menu"]="मेनू बंद करें", ["common.close"]="बंद करें", ["common.back"]="वापस",
            ["common.previous"]="पिछला", ["common.next"]="अगला", ["common.page_format"]="पृष्ठ {0} / {1}",
            ["common.delete"]="हटाएँ", ["common.cancel"]="रद्द करें", ["common.confirm"]="पुष्टि करें",
            ["common.yes"]="हाँ", ["common.no"]="नहीं", ["common.retry"]="फिर प्रयास करें", ["common.menu"]="मेनू",
            ["settings.recording.title"]="रिकॉर्डिंग सेटिंग्स", ["settings.recording_hand"]="रिकॉर्डिंग वाला हाथ",
            ["settings.left"]="बायाँ", ["settings.right"]="दायाँ", ["settings.conversational"]="संवाद मोड",
            ["settings.manual"]="मैन्युअल", ["settings.subtitles_on"]="उपशीर्षक चालू",
            ["settings.subtitles_off"]="उपशीर्षक बंद", ["settings.edit_name"]="नाम बदलें",
            ["settings.replay_tutorial"]="ट्यूटोरियल फिर चलाएँ", ["settings.wrist.title"]="कलाई मेनू",
            ["settings.wrist.left"]="वॉच: बायाँ", ["settings.wrist.right"]="वॉच: दायाँ",
            ["settings.interface_language"]="इंटरफ़ेस भाषा", ["settings.language.title"]="भाषा सेटिंग्स",
            ["settings.conversation_mode"]="वॉइस भाषा", ["settings.conversation.active"]="वॉइस भाषा स्वचालित पहचान और सेवा क्षमता से तय होती है।",
            ["settings.decrease_silence"]="मौन प्रतीक्षा घटाएँ", ["settings.increase_silence"]="मौन प्रतीक्षा बढ़ाएँ",
            ["settings.seconds_format"]="{0:0.0} सेकंड", ["settings.language.auto"]="स्वचालित",
            ["language.english"]="अंग्रेज़ी", ["language.spanish"]="स्पेनिश", ["language.french"]="फ़्रेंच",
            ["language.mandarin"]="सरलीकृत चीनी", ["language.hindi"]="हिन्दी",
            ["language.confirm.body"]="इंटरफ़ेस भाषा को {0} में बदलें? इससे संवाद भाषा या Orby की आवाज़ नहीं बदलेगी।",
            ["settings.voice_language.info"]="वाणी, उपशीर्षक और Orby की आवाज़ की भाषा संवाद सेटिंग और सेवा क्षमता से अलग तय होती है। इंटरफ़ेस भाषा उन्हें नहीं बदलती।",
            ["pairing.expires"]="कोड {0} में समाप्त होगा",
            ["session.delete.title"]="सत्र हटाएँ", ["session.delete.body"]="“{0}” हटाएँ? इससे केवल यह स्थानीय सत्र हटेगा।",
            ["transcript.transcribing"]="लिप्यंतरण हो रहा है…", ["transcript.complete"]="लिप्यंतरण पूर्ण",
            ["transcript.failed"]="लिप्यंतरण विफल", ["transcript.back"]="रिकॉर्डिंग पर वापस जाएँ",
            ["ai.response"]="AI उत्तर", ["ai.clarification"]="Orby को स्पष्टीकरण चाहिए", ["ai.record_answer"]="उत्तर रिकॉर्ड करें",
            ["state.idle"]="निष्क्रिय", ["state.listening"]="सुन रहा है", ["state.thinking"]="सोच रहा है",
            ["state.speaking"]="बोल रहा है", ["state.reviewing"]="समीक्षा हो रही है", ["state.error"]="त्रुटि", ["state.paused"]="रुका हुआ",
            ["hud.mic_on"]="माइक चालू", ["hud.mic_off"]="माइक बंद",
            ["hud.mic_off_finishing"]="माइक बंद · वर्तमान विचार पूरा हो रहा है", ["hud.mic_idle"]="माइक चालू · निष्क्रिय",
            ["hud.speech_detected"]="वाणी मिली", ["hud.finishing"]="विचार पूरा हो रहा है",
            ["orby.status"]="Orby · {0}", ["orby.finishing"]="Orby · विचार पूरा कर रहा है…",
            ["ai.request_failed"]="AI अनुरोध विफल", ["ai.clarification_ended"]="स्पष्टीकरण समाप्त",
            ["ai.invalid_response"]="AI उत्तर अमान्य", ["ai.idea_failed"]="विचार संसाधन विफल",
            ["ai.idea_save_failed"]="विचार सहेजना विफल", ["ai.visual_unavailable"]="विचार का दृश्य उपलब्ध नहीं",
            ["ai.voice_unavailable"]="आवाज़ उपलब्ध नहीं",
            ["recording.complete"]="रिकॉर्डिंग पूर्ण", ["recording.play"]="रिकॉर्डिंग चलाएँ",
            ["recording.stop"]="प्लेबैक रोकें", ["recording.discard"]="हटाएँ",
            ["recording.rerecord"]="फिर रिकॉर्ड करें", ["recording.send_orby"]="Orby को भेजें",
            ["idea.details"]="विचार विवरण", ["idea.summary"]="सारांश", ["idea.category"]="श्रेणी",
            ["idea.color"]="रंग", ["idea.delete"]="विचार हटाएँ",
            ["session.delete.confirm"]="सत्र हटाएँ", ["transfer.choose_import"]="आयात करने के लिए Thinking Space JSON सत्र चुनें।"
        };
        private static Dictionary<string,ThinkingSpaceStringEntry> BuildByKey()
        { var result=new Dictionary<string,ThinkingSpaceStringEntry>(StringComparer.Ordinal); foreach(var entry in Entries)result[entry.Key]=entry;return result; }
        private static Dictionary<string,string> BuildByEnglish()
        { var result=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var entry in Entries)if(!result.ContainsKey(entry.English))result[entry.English]=entry.Key;return result; }
        public static bool TryGet(string key,out ThinkingSpaceStringEntry entry)=>ByKey.TryGetValue(key,out entry);
        public static bool TryGetChinese(string key,out string value)=>Chinese.TryGetValue(key??string.Empty,out value);
        public static bool TryGetHindi(string key,out string value)=>Hindi.TryGetValue(key??string.Empty,out value);
        public static bool TryGetKeyForEnglish(string english,out string key)=>KeyByEnglish.TryGetValue(english??string.Empty,out key);
    }

    [DisallowMultipleComponent]
    public sealed class ThinkingSpaceLocalizationController : MonoBehaviour
    {
        public static ThinkingSpaceLocalizationController Instance { get; private set; }
        public static event Action<string> InterfaceLanguageChanged;
        public string InterfaceLanguageCode { get; private set; } = ThinkingSpacePreferencesStore.DefaultInterfaceLanguageCode;
        public static IReadOnlyList<string> SupportedInterfaceLanguages => ThinkingSpaceStringCatalog.SupportedLocaleCodes;
        public static IReadOnlyList<string> PreparedInterfaceLanguages => ThinkingSpaceStringCatalog.PreparedLocaleCodes;
        private ThinkingSpacePreferencesStore preferences;
        private string pendingInterfaceLanguage;
        private float pendingLanguageExpiresAt;
        public string PendingInterfaceLanguage => pendingInterfaceLanguage;
        public bool HasPendingInterfaceLanguageChange => !string.IsNullOrEmpty(pendingInterfaceLanguage);
        public static event Action<string> InterfaceLanguageConfirmationRequested;

        public static ThinkingSpaceLocalizationController Ensure(ThinkingSpacePreferencesStore store)
        {
            if(Instance==null)
            {
                Instance=FindFirstObjectByType<ThinkingSpaceLocalizationController>();
                if(Instance==null)
                {
                    var root=new GameObject("Thinking Space Localization");
                    Instance=root.AddComponent<ThinkingSpaceLocalizationController>();
                    DontDestroyOnLoad(root);
                }
            }
            Instance.Initialize(store);
            return Instance;
        }

        private void Awake()
        {
            if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}
            Instance=this;
        }

        private void OnDestroy()
        {
            if(Instance==this)Instance=null;
        }

        private void Update()
        {
            if(HasPendingInterfaceLanguageChange&&Time.unscaledTime>=pendingLanguageExpiresAt)
                CancelPendingInterfaceLanguageChange();
        }

        public bool RequestInterfaceLanguageChange(string languageCode,float timeoutSeconds=20f)
        {
            string normalized=ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage(languageCode);
            if(normalized==null||normalized==InterfaceLanguageCode)return false;
            pendingInterfaceLanguage=normalized;
            pendingLanguageExpiresAt=Time.unscaledTime+Mathf.Clamp(timeoutSeconds,5f,60f);
            InterfaceLanguageConfirmationRequested?.Invoke(normalized);
            return true;
        }

        public bool ConfirmPendingInterfaceLanguageChange()
        {
            if(!HasPendingInterfaceLanguageChange)return false;
            string language=pendingInterfaceLanguage;
            CancelPendingInterfaceLanguageChange();
            return SetInterfaceLanguage(language);
        }

        public void CancelPendingInterfaceLanguageChange()
        {pendingInterfaceLanguage=null;pendingLanguageExpiresAt=0f;}

        private void Initialize(ThinkingSpacePreferencesStore store)
        {
            preferences=store;
            ApplyLanguage(store?.Data.InterfaceLanguageCode??ThinkingSpacePreferencesStore.DefaultInterfaceLanguageCode,false);
        }

        public bool SetInterfaceLanguage(string languageCode)
        {
            string normalized=ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage(languageCode);
            if(normalized==null)return false;
            string previous=InterfaceLanguageCode;
            if(preferences!=null&&!preferences.SetInterfaceLanguage(normalized))return false;
            if(!ApplyLanguage(normalized,true))
            {
                if(preferences!=null)preferences.SetInterfaceLanguage(previous);
                ApplyLanguage(previous,true);
                return false;
            }
            return true;
        }

        private bool ApplyLanguage(string languageCode,bool notify)
        {
            string normalized=ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage(languageCode)
                ?? ThinkingSpacePreferencesStore.DefaultInterfaceLanguageCode;
            InterfaceLanguageCode=normalized;
            try
            {
                Locale locale=LocalizationSettings.AvailableLocales?.GetLocale(new LocaleIdentifier(normalized));
                if(locale!=null)LocalizationSettings.SelectedLocale=locale;
            }
            catch(Exception exception){Debug.LogWarning("Localization locale selection failed: "+exception.Message);}
            if(notify)InterfaceLanguageChanged?.Invoke(normalized);
            return true;
        }

        public static string Get(string key,params object[] arguments)
            =>TranslateForLanguage(Instance?.InterfaceLanguageCode??ThinkingSpacePreferencesStore.DefaultInterfaceLanguageCode,key,arguments);

        public static string TranslateForLanguage(string languageCode,string key,params object[] arguments)
        {
            if(!ThinkingSpaceStringCatalog.TryGet(key,out ThinkingSpaceStringEntry entry))return key??string.Empty;
            string raw=(languageCode??string.Empty).Trim().Replace('_','-').ToLowerInvariant();
            string normalized=raw=="zh"||raw=="zh-cn"?"zh-cn":
                ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage(raw)??"en";
            string template=normalized=="es"?entry.Spanish:normalized=="fr"?entry.French:
                normalized=="zh-cn"&&ThinkingSpaceStringCatalog.TryGetChinese(key,out string chinese)?chinese:
                normalized.Equals("hi-IN",StringComparison.OrdinalIgnoreCase)&&
                    ThinkingSpaceStringCatalog.TryGetHindi(key,out string hindi)?hindi:entry.English;
            if(string.IsNullOrWhiteSpace(template))template=entry.English;
            if(arguments==null||arguments.Length==0)return template;
            try{return string.Format(CultureInfo.GetCultureInfo(normalized),template,arguments);}
            catch(FormatException){return string.Format(CultureInfo.InvariantCulture,entry.English,arguments);}
        }

        public static void LocalizeKnownText(GameObject root)
        {
            if(root==null)return;
            foreach(TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if(text==null)continue;
                ThinkingSpaceLocalizedText marker=text.GetComponent<ThinkingSpaceLocalizedText>();
                if(marker!=null){marker.Refresh();continue;}
                if(ThinkingSpaceStringCatalog.TryGetKeyForEnglish(text.text?.Trim(),out string key))
                    text.gameObject.AddComponent<ThinkingSpaceLocalizedText>().Configure(key);
            }
        }

        public static string RuntimeFontResourcePathForLanguage(string languageCode)
        {
            string normalized=ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage(languageCode)??"en";
            return normalized=="hi-IN"?"Fonts/NotoSansDevanagari-Regular SDF":
                normalized=="zh-CN"?"Fonts/NotoSansCJKsc-Regular SDF":string.Empty;
        }

        public static TMP_FontAsset RuntimeFontForLanguage(string languageCode)
        {
            string path=RuntimeFontResourcePathForLanguage(languageCode);
            return string.IsNullOrEmpty(path)?null:Resources.Load<TMP_FontAsset>(path);
        }
    }

    [DisallowMultipleComponent]
    public sealed class ThinkingSpaceLocalizedText : MonoBehaviour
    {
        [SerializeField] private string stringKey;
        private TMP_Text target;
        public string StringKey=>stringKey;
        public void Configure(string key){stringKey=key;Refresh();}
        private void Awake(){target=GetComponent<TMP_Text>();}
        private void OnEnable(){ThinkingSpaceLocalizationController.InterfaceLanguageChanged+=HandleChanged;Refresh();}
        private void OnDisable(){ThinkingSpaceLocalizationController.InterfaceLanguageChanged-=HandleChanged;}
        private void HandleChanged(string _)=>Refresh();
        public void Refresh()
        {
            if(target==null)target=GetComponent<TMP_Text>();
            if(target==null)return;
            if(target.font==null)target.font=TMP_Settings.defaultFontAsset;
            TMP_FontAsset fallback=ThinkingSpaceLocalizationController.RuntimeFontForLanguage(
                ThinkingSpaceLocalizationController.Instance?.InterfaceLanguageCode??
                ThinkingSpacePreferencesStore.DefaultInterfaceLanguageCode);
            if(target.font!=null&&fallback!=null&&target.font!=fallback)
            {
                if(target.font.fallbackFontAssetTable==null)
                    target.font.fallbackFontAssetTable=new System.Collections.Generic.List<TMP_FontAsset>();
                if(!target.font.fallbackFontAssetTable.Contains(fallback))
                    target.font.fallbackFontAssetTable.Add(fallback);
            }
            if(!string.IsNullOrEmpty(stringKey))target.text=ThinkingSpaceLocalizationController.Get(stringKey);
        }
    }
}
