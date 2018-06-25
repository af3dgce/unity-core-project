﻿using System;
using System.Threading.Tasks;
using Core.Services.Assets;
using Core.Services.UI;
using UniRx;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace Core.Services.Scenes
{
    public class SceneLoaderService : Service
    {
#pragma warning disable 0414    // suppress value not used warning
        private SceneLoaderServiceConfiguration _configuration;
#pragma warning restore 0414    // restore value not used warning

        [Inject]
        private AssetService _assetService;

        [Inject]
        private UIService _uiService;

        public SceneLoaderService(ServiceConfiguration config)
        {
            _configuration = config as SceneLoaderServiceConfiguration;
        }

        /// <summary>
        /// Attempts to load a scene from an asset bundle
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="mode"> </param>
        /// <returns></returns>
        public async Task<UnityEngine.Object> LoadScene(string scene, LoadSceneMode mode = LoadSceneMode.Single)
        {
            _uiService.DarkenScreen(true).Subscribe();
            return await GetScene(scene, mode);
        }

        /// <summary>
        /// Gets a scene from an asset bundle
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="mode"> </param>
        /// <returns></returns>
        private async Task<UnityEngine.Object> GetScene(string scene, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (_assetService.GetLoadedBundle<UnityEngine.Object>(scene))
                throw new Exception("Scene " + scene + " is already loaded and open. Opening the same scene twice is not supported.");

            var sceneObject = await _assetService.GetScene(new BundleRequest(AssetCategoryRoot.Scenes, scene, scene, _assetService.Configuration));

            if (sceneObject)
            {
                Debug.Log(("SceneLoaderService: Loaded scene - " + scene).Colored(Colors.LightBlue));

                await SceneManager.LoadSceneAsync(scene, mode);
                // await Resources.UnloadUnusedAssets();
                //Scene loaded, return screen to normal.
                _uiService.DarkenScreen(false).Subscribe();
                Debug.Log(("SceneLoaderService: Opened scene - " + scene).Colored(Colors.LightBlue));
            }

            return sceneObject;
        }

        /// <summary>
        /// Unload scene.
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        public async Task UnLoadScene(string scene)
        {
            await SceneManager.UnloadSceneAsync(scene);

            Debug.Log(("SceneLoaderService: Unloaded scene - " + scene).Colored(Colors.LightBlue));

            _assetService.UnloadAsset(scene, true);
        }
    }
}