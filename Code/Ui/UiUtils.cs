﻿using Flowframes.Data;
using Flowframes.IO;
using Flowframes.Main;
using Flowframes.Os;
using System;
using System.Windows.Forms;

namespace Flowframes.Ui
{
    class UiUtils
    {
		public static void InitCombox(ComboBox box, int index)
		{
			if (box.Items.Count >= 1)
			{
				box.SelectedIndex = index;
				box.Text = box.Items[index].ToString();
			}
		}

		public static bool AssignComboxIndexFromText (ComboBox box, string text)	// Set index to corresponding text
        {
			int index = box.Items.IndexOf(text);

			if (index == -1)    // custom value, index not found
				return false;

			box.SelectedIndex = index;
			return true;
		}

		public static ComboBox LoadAiModelsIntoGui (ComboBox combox, AI ai)
        {
			combox.Items.Clear();

            try
            {
				ModelCollection modelCollection = AiModels.GetModels(ai);

				for (int i = 0; i < modelCollection.models.Count; i++)
				{
					ModelCollection.ModelInfo modelInfo = modelCollection.models[i];

					if (string.IsNullOrWhiteSpace(modelInfo.name))
						continue;

					combox.Items.Add(modelInfo.GetUiString());

					if (modelInfo.isDefault)
						combox.SelectedIndex = i;
				}

				if (combox.SelectedIndex < 0)
					combox.SelectedIndex = 0;

				SelectNcnnIfNoCudaAvail(combox);
			}
            catch (Exception e)
            {
				Logger.Log($"Failed to load available AI models for {ai.aiName}! {e.Message}");
				Logger.Log($"Stack Trace: {e.StackTrace}", true);
            }

			return combox;
		}

		static void SelectNcnnIfNoCudaAvail (ComboBox combox)
        {
			if(NvApi.gpuList.Count < 1)
            {
				for(int i = 0; i < combox.Items.Count; i++)
                {
					if (((string)combox.Items[i]).ToUpper().Contains("NCNN"))
						combox.SelectedIndex = i;
                }
            }
        }
	}
}
