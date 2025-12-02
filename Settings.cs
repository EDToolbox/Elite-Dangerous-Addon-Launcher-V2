namespace Elite_Dangerous_Addon_Launcher_V2
{
    public class Settings
    {
        #region Public Properties

        public bool CloseAllAppsOnExit { get; set; } = false;

        private string _theme = AppConstants.DefaultTheme;
        public string Theme
        {
            get => _theme;
            set => _theme = value ?? AppConstants.DefaultTheme;
        }

        private string _language = "auto";
        /// <summary>
        /// The application language. "auto" = system language, or specific culture code like "en", "de"
        /// </summary>
        public string Language
        {
            get => _language;
            set => _language = value ?? "auto";
        }

        #endregion Public Properties
    }
}
