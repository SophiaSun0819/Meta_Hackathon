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
        public static readonly string[] SupportedLocaleCodes = { "en", "es", "fr" };
        public static readonly string[] PreparedLocaleCodes = { "zh-CN", "hi-IN" };

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
            new("menu.challenge", "Challenge My Idea", "Cuestionar mi idea", "Remettre mon idée en question"),
            new("menu.expand", "Expand My Idea", "Ampliar mi idea", "Développer mon idée"),
            new("menu.connect", "Connect My Ideas", "Conectar mis ideas", "Relier mes idées"),
            new("menu.whiteboard", "Spawn Whiteboard", "Crear pizarra", "Créer un tableau"),
            new("menu.help.voice", "Voice command reference will be added with speech recognition.", "La guía de comandos de voz se añadirá con el reconocimiento de voz.", "Le guide des commandes vocales sera ajouté avec la reconnaissance vocale."),
            new("menu.help.gestures", "Gesture reference will be expanded as interactions are finalized.", "La guía de gestos se ampliará al finalizar las interacciones.", "Le guide des gestes sera complété à mesure que les interactions seront finalisées."),
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
            new("settings.language.auto", "Automatic", "Automático", "Automatique"),
            new("settings.language.specific", "Specific Language", "Idioma específico", "Langue spécifique"),
            new("settings.voice_profile", "Orby Voice", "Voz de Orby", "Voix d'Orby"),
            new("settings.voice.auto", "Automatic Voice", "Voz automática", "Voix automatique"),
            new("language.confirm.title", "Change Interface Language?", "¿Cambiar el idioma de la interfaz?", "Changer la langue de l'interface ?"),
            new("language.confirm.body", "Change the interface to {0}?", "¿Cambiar la interfaz a {0}?", "Changer l'interface en {0} ?"),
            new("language.unsupported_ui", "That language is available for conversation, but its interface translation is not ready.", "Ese idioma está disponible para conversar, pero la interfaz aún no está traducida.", "Cette langue est disponible pour la conversation, mais son interface n'est pas encore traduite."),
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
            ["common.close_menu"]="关闭菜单", ["common.close"]="关闭", ["common.back"]="返回",
            ["common.previous"]="上一页", ["common.next"]="下一页", ["common.page_format"]="第 {0} / {1} 页",
            ["common.delete"]="删除", ["common.cancel"]="取消", ["common.confirm"]="确认",
            ["common.yes"]="是", ["common.no"]="否", ["common.retry"]="重试", ["common.menu"]="菜单",
            ["settings.recording.title"]="录音设置", ["settings.recording_hand"]="录音手",
            ["settings.left"]="左手", ["settings.right"]="右手", ["settings.conversational"]="对话模式",
            ["settings.manual"]="手动模式", ["settings.subtitles_on"]="字幕开启",
            ["settings.subtitles_off"]="字幕关闭", ["settings.edit_name"]="编辑姓名",
            ["settings.replay_tutorial"]="重播教程", ["settings.wrist.title"]="腕部菜单",
            ["settings.interface_language"]="界面语言", ["settings.language.title"]="语言设置",
            ["settings.conversation_mode"]="对话语言", ["settings.language.auto"]="自动",
            ["settings.language.specific"]="指定语言", ["settings.voice_profile"]="Orby 语音",
            ["settings.voice.auto"]="自动语音", ["language.english"]="英语", ["language.spanish"]="西班牙语",
            ["language.french"]="法语", ["language.mandarin"]="普通话", ["language.hindi"]="印地语",
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
            ["hud.speech_detected"]="检测到语音", ["hud.finishing"]="正在结束发言",
            ["onboarding.name_prompt"]="Orby 应该怎么称呼你？", ["onboarding.skip_name"]="跳过姓名",
            ["onboarding.skip_tutorial"]="跳过教程", ["onboarding.space"]="空格",
            ["gesture.hold_fist"]="保持握拳", ["recording.active"]="● 正在录音",
            ["color.category"]="类别", ["color.blue"]="蓝色", ["color.cyan"]="青色",
            ["color.green"]="绿色", ["color.yellow"]="黄色", ["color.orange"]="橙色",
            ["color.red"]="红色", ["color.pink"]="粉色", ["color.purple"]="紫色"
        };
        private static Dictionary<string,ThinkingSpaceStringEntry> BuildByKey()
        { var result=new Dictionary<string,ThinkingSpaceStringEntry>(StringComparer.Ordinal); foreach(var entry in Entries)result[entry.Key]=entry;return result; }
        private static Dictionary<string,string> BuildByEnglish()
        { var result=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var entry in Entries)if(!result.ContainsKey(entry.English))result[entry.English]=entry.Key;return result; }
        public static bool TryGet(string key,out ThinkingSpaceStringEntry entry)=>ByKey.TryGetValue(key,out entry);
        public static bool TryGetChinese(string key,out string value)=>Chinese.TryGetValue(key??string.Empty,out value);
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
                normalized=="zh-cn"&&ThinkingSpaceStringCatalog.TryGetChinese(key,out string chinese)?chinese:entry.English;
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
            TMP_FontAsset fallback=TMP_Settings.defaultFontAsset;
            if(target.font!=null&&fallback!=null&&target.font!=fallback
                &&target.font.fallbackFontAssetTable!=null
                &&!target.font.fallbackFontAssetTable.Contains(fallback))
                target.font.fallbackFontAssetTable.Add(fallback);
            if(!string.IsNullOrEmpty(stringKey))target.text=ThinkingSpaceLocalizationController.Get(stringKey);
        }
    }
}
