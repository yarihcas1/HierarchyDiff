using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class HierarchyDiff : EditorWindow
{
    private GameObject objectA;
    private GameObject objectB;
    private Vector2 scrollPosition;
    private string componentFilter = "";
    private string propertyFilter = "";
    private bool showIdenticalValues = false;
    private bool expandAll = false;
    private bool ignoreObjectReferenceDifferences = false;
    private bool remapObjectReferences = true;
    private bool hideIdenticalHierarchies = false;
    
    // Style caches
    private GUIStyle differenceStyle;
    private GUIStyle headerDifferenceStyle;
    private GUIStyle identicalStyle;
    
    private Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();
    private Dictionary<string, Dictionary<string, bool>> componentFoldouts = new Dictionary<string, Dictionary<string, bool>>();
    
    [MenuItem("Tools/yarihcas1_lab/Hierarchy Diff")]
    public static void ShowWindow()
    {
        var window = GetWindow<HierarchyDiff>("Hierarchy Diff");
        window.minSize = new Vector2(800, 600);
    }
    
    private void InitializeStyles()
    {
        if (differenceStyle == null)
        {
            differenceStyle = new GUIStyle(EditorStyles.label);
            differenceStyle.normal.textColor = new Color(0.9f, 0.2f, 0.2f); // Red
            differenceStyle.fontStyle = FontStyle.Bold;
        }
        
        if (headerDifferenceStyle == null)
        {
            headerDifferenceStyle = new GUIStyle(EditorStyles.boldLabel);
            headerDifferenceStyle.normal.textColor = new Color(0.9f, 0.3f, 0.1f); // Orange-red
        }
        
        if (identicalStyle == null)
        {
            identicalStyle = new GUIStyle(EditorStyles.label);
            identicalStyle.normal.textColor = new Color(0.4f, 0.7f, 0.4f); // Green
        }
    }
    
    private void OnGUI()
    {
        InitializeStyles();
        
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("階層構造差分ツール", EditorStyles.boldLabel);
        
        // Object selection
        EditorGUILayout.BeginHorizontal();
        objectA = (GameObject)EditorGUILayout.ObjectField("オブジェクト A", objectA, typeof(GameObject), true);
        objectB = (GameObject)EditorGUILayout.ObjectField("オブジェクト B", objectB, typeof(GameObject), true);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        
        // Filters
        EditorGUILayout.LabelField("フィルター設定", EditorStyles.boldLabel);
        componentFilter = EditorGUILayout.TextField("コンポーネント名フィルター", componentFilter);
        propertyFilter = EditorGUILayout.TextField("プロパティ名フィルター", propertyFilter);
        
        EditorGUILayout.BeginHorizontal();
        showIdenticalValues = EditorGUILayout.Toggle("同一値も表示", showIdenticalValues);
        ignoreObjectReferenceDifferences = EditorGUILayout.Toggle("参照の違いを無視", ignoreObjectReferenceDifferences);
        if (GUILayout.Button(expandAll ? "全て折りたたむ" : "全て展開"))
        {
            expandAll = !expandAll;
            SetAllFoldouts(expandAll);
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        remapObjectReferences = EditorGUILayout.Toggle("参照を自動リマップ", remapObjectReferences);
        hideIdenticalHierarchies = EditorGUILayout.Toggle("差分のない階層を非表示", hideIdenticalHierarchies);
        EditorGUILayout.EndHorizontal();
        
        if (ignoreObjectReferenceDifferences)
        {
            EditorGUILayout.HelpBox("同じ名前・相対パスのオブジェクト参照は同一とみなします", MessageType.Info);
        }
        
        if (remapObjectReferences)
        {
            EditorGUILayout.HelpBox("コピー時、参照をコピー先プレファブ内の同じ位置のオブジェクトに自動変換します", MessageType.Info);
        }
        
        if (hideIdenticalHierarchies)
        {
            EditorGUILayout.HelpBox("差分がある階層のみ表示します", MessageType.Info);
        }
        
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        if (objectA == null || objectB == null)
        {
            EditorGUILayout.HelpBox("比較するオブジェクトA, Bを両方指定してください", MessageType.Info);
            return;
        }

        // グローバル一括コピーボタン
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("全て A→B", GUILayout.Width(100)))
        {
            var differences = CollectAllDifferences();
            BulkCopyProperties(differences, true);
        }
        if (GUILayout.Button("全て B→A", GUILayout.Width(100)))
        {
            var differences = CollectAllDifferences();
            BulkCopyProperties(differences, false);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Comparison display
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        CompareHierarchy(objectA.transform, objectB.transform, "");
        EditorGUILayout.EndScrollView();
    }
    
    private void SetAllFoldouts(bool state)
    {
        var keys = foldoutStates.Keys.ToList();
        foreach (var key in keys)
        {
            foldoutStates[key] = state;
        }
        
        foreach (var componentDict in componentFoldouts.Values)
        {
            var componentKeys = componentDict.Keys.ToList();
            foreach (var key in componentKeys)
            {
                componentDict[key] = state;
            }
        }
    }
    
    private void CompareHierarchy(Transform transformA, Transform transformB, string path)
    {
        string currentPath = path + "/" + transformA.name;
        
        // Check if this hierarchy has any differences
        if (hideIdenticalHierarchies)
        {
            if (!HasDifferencesInHierarchy(transformA, transformB))
            {
                // No differences found, skip this hierarchy
                return;
            }
        }
        
        if (!foldoutStates.ContainsKey(currentPath))
            foldoutStates[currentPath] = false;
        
        EditorGUILayout.BeginVertical("box");
        
        // Hierarchy node header
        EditorGUILayout.BeginHorizontal();
        bool shouldExpandHierarchy = expandAll || foldoutStates[currentPath];
        foldoutStates[currentPath] = EditorGUILayout.Foldout(shouldExpandHierarchy, transformA.name, true);

        if (transformA.name != transformB.name)
        {
            EditorGUILayout.LabelField($"⚠ 名前不一致: {transformB.name}", EditorStyles.miniLabel);
        }

        GUILayout.FlexibleSpace();

        // 階層ノード用一括コピーボタン
        if (GUILayout.Button("A→B", GUILayout.Width(45)))
        {
            var differences = CollectNodeDifferences(transformA.gameObject, transformB.gameObject);
            BulkCopyProperties(differences, true);
        }
        if (GUILayout.Button("B→A", GUILayout.Width(45)))
        {
            var differences = CollectNodeDifferences(transformA.gameObject, transformB.gameObject);
            BulkCopyProperties(differences, false);
        }

        EditorGUILayout.EndHorizontal();
        
        if (foldoutStates[currentPath])
        {
            EditorGUI.indentLevel++;
            
            // Compare components
            CompareComponents(transformA.gameObject, transformB.gameObject, currentPath);
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.EndVertical();
        
        // Compare children
        if (foldoutStates[currentPath])
        {
            EditorGUI.indentLevel++;
            
            // Match children by name
            Dictionary<string, Transform> childrenA = new Dictionary<string, Transform>();
            Dictionary<string, Transform> childrenB = new Dictionary<string, Transform>();
            
            for (int i = 0; i < transformA.childCount; i++)
            {
                Transform child = transformA.GetChild(i);
                childrenA[child.name] = child;
            }
            
            for (int i = 0; i < transformB.childCount; i++)
            {
                Transform child = transformB.GetChild(i);
                childrenB[child.name] = child;
            }
            
            foreach (var kvp in childrenA)
            {
                if (childrenB.ContainsKey(kvp.Key))
                {
                    CompareHierarchy(kvp.Value, childrenB[kvp.Key], currentPath);
                }
                else
                {
                    EditorGUILayout.HelpBox($"オブジェクトBに存在しない: {kvp.Key}", MessageType.Warning);
                }
            }
            
            foreach (var kvp in childrenB)
            {
                if (!childrenA.ContainsKey(kvp.Key))
                {
                    EditorGUILayout.HelpBox($"オブジェクトAに存在しない: {kvp.Key}", MessageType.Warning);
                }
            }
            
            EditorGUI.indentLevel--;
        }
    }
    
    private bool HasDifferencesInHierarchy(Transform transformA, Transform transformB)
    {
        // Check if names are different
        if (transformA.name != transformB.name)
            return true;

        // Check components for differences
        Component[] componentsA = transformA.gameObject.GetComponents<Component>();
        Component[] componentsB = transformB.gameObject.GetComponents<Component>();

        // 同じ型のコンポーネントをインデックスでマッチングするためにグループ化
        var componentsByTypeA = new Dictionary<System.Type, List<Component>>();
        var componentsByTypeB = new Dictionary<System.Type, List<Component>>();

        foreach (var comp in componentsA)
        {
            if (comp == null) continue;
            var type = comp.GetType();
            if (!componentsByTypeA.ContainsKey(type))
                componentsByTypeA[type] = new List<Component>();
            componentsByTypeA[type].Add(comp);
        }

        foreach (var comp in componentsB)
        {
            if (comp == null) continue;
            var type = comp.GetType();
            if (!componentsByTypeB.ContainsKey(type))
                componentsByTypeB[type] = new List<Component>();
            componentsByTypeB[type].Add(comp);
        }

        foreach (var kvp in componentsByTypeA)
        {
            System.Type componentTypeKey = kvp.Key;
            List<Component> listA = kvp.Value;
            string componentTypeName = componentTypeKey.Name;

            // Apply component filter
            if (!string.IsNullOrEmpty(componentFilter) &&
                !componentTypeName.ToLower().Contains(componentFilter.ToLower()))
                continue;

            // Bに同じ型のコンポーネントがない場合は差分あり
            if (!componentsByTypeB.ContainsKey(componentTypeKey))
                return true;

            List<Component> listB = componentsByTypeB[componentTypeKey];

            // コンポーネント数が異なる場合は差分あり
            if (listA.Count != listB.Count)
                return true;

            // インデックスでマッチングして比較
            for (int i = 0; i < listA.Count; i++)
            {
                int differenceCount = CountDifferences(listA[i], listB[i]);
                if (differenceCount > 0)
                    return true;
            }
        }
        
        // Check children for differences
        Dictionary<string, Transform> childrenA = new Dictionary<string, Transform>();
        Dictionary<string, Transform> childrenB = new Dictionary<string, Transform>();
        
        for (int i = 0; i < transformA.childCount; i++)
        {
            Transform child = transformA.GetChild(i);
            childrenA[child.name] = child;
        }
        
        for (int i = 0; i < transformB.childCount; i++)
        {
            Transform child = transformB.GetChild(i);
            childrenB[child.name] = child;
        }
        
        // Check for missing children
        foreach (var kvp in childrenA)
        {
            if (!childrenB.ContainsKey(kvp.Key))
                return true; // Child missing in B
        }
        
        foreach (var kvp in childrenB)
        {
            if (!childrenA.ContainsKey(kvp.Key))
                return true; // Child missing in A
        }
        
        // Recursively check children
        foreach (var kvp in childrenA)
        {
            if (childrenB.ContainsKey(kvp.Key))
            {
                if (HasDifferencesInHierarchy(kvp.Value, childrenB[kvp.Key]))
                    return true;
            }
        }
        
        // No differences found
        return false;
    }
    
    private void CompareComponents(GameObject objA, GameObject objB, string path)
    {
        Component[] componentsA = objA.GetComponents<Component>();
        Component[] componentsB = objB.GetComponents<Component>();

        if (!componentFoldouts.ContainsKey(path))
            componentFoldouts[path] = new Dictionary<string, bool>();

        // 同じ型のコンポーネントをインデックスでマッチングするためにグループ化
        var componentsByTypeA = new Dictionary<System.Type, List<Component>>();
        var componentsByTypeB = new Dictionary<System.Type, List<Component>>();

        foreach (var comp in componentsA)
        {
            if (comp == null) continue;
            var type = comp.GetType();
            if (!componentsByTypeA.ContainsKey(type))
                componentsByTypeA[type] = new List<Component>();
            componentsByTypeA[type].Add(comp);
        }

        foreach (var comp in componentsB)
        {
            if (comp == null) continue;
            var type = comp.GetType();
            if (!componentsByTypeB.ContainsKey(type))
                componentsByTypeB[type] = new List<Component>();
            componentsByTypeB[type].Add(comp);
        }

        foreach (var kvp in componentsByTypeA)
        {
            System.Type componentTypeKey = kvp.Key;
            List<Component> listA = kvp.Value;
            string componentTypeName = componentTypeKey.Name;

            // Apply component filter
            if (!string.IsNullOrEmpty(componentFilter) &&
                !componentTypeName.ToLower().Contains(componentFilter.ToLower()))
                continue;

            // Bに同じ型のコンポーネントがあるか確認
            if (!componentsByTypeB.ContainsKey(componentTypeKey))
            {
                EditorGUILayout.HelpBox($"コンポーネント '{componentTypeName}' がBに存在しません", MessageType.Warning);
                continue;
            }

            List<Component> listB = componentsByTypeB[componentTypeKey];
            int count = System.Math.Min(listA.Count, listB.Count);

            // インデックスでマッチング
            for (int i = 0; i < count; i++)
            {
                Component componentA = listA[i];
                Component componentB = listB[i];

                // 複数ある場合はインデックス付きの表示名を使用
                string displayName = listA.Count > 1 ? $"{componentTypeName} [{i}]" : componentTypeName;
                string componentKey = listA.Count > 1 ? $"{componentTypeName}_{i}" : componentTypeName;

                if (!componentFoldouts[path].ContainsKey(componentKey))
                    componentFoldouts[path][componentKey] = false;

                // Count differences
                int differenceCount = CountDifferences(componentA, componentB);

                EditorGUILayout.BeginVertical("box");

                // Temporarily remove indent for the header row
                int originalIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;

                EditorGUILayout.BeginHorizontal();

                // Show difference indicator
                if (differenceCount > 0)
                {
                    GUIStyle iconStyle = new GUIStyle(EditorStyles.label);
                    iconStyle.normal.textColor = new Color(0.9f, 0.2f, 0.2f);
                    iconStyle.fontSize = 14;
                    iconStyle.fontStyle = FontStyle.Bold;
                    EditorGUILayout.LabelField("●", iconStyle, GUILayout.Width(15));
                }
                else
                {
                    GUIStyle iconStyle = new GUIStyle(EditorStyles.label);
                    iconStyle.normal.textColor = new Color(0.4f, 0.7f, 0.4f);
                    iconStyle.fontSize = 14;
                    EditorGUILayout.LabelField("✓", iconStyle, GUILayout.Width(15));
                }

                // Add indent spacing manually
                GUILayout.Space(originalIndent * 15);

                bool shouldExpandComponent = expandAll || componentFoldouts[path][componentKey];
                componentFoldouts[path][componentKey] = EditorGUILayout.Foldout(
                    shouldExpandComponent,
                    displayName,
                    true
                );

                // Show difference count - always left aligned
                if (differenceCount > 0)
                {
                    EditorGUILayout.LabelField($"({differenceCount}件の差分)", headerDifferenceStyle, GUILayout.Width(100));
                }
                else
                {
                    EditorGUILayout.LabelField("(同一)", identicalStyle, GUILayout.Width(60));
                }

                GUILayout.FlexibleSpace();

                // コンポーネント用一括コピーボタン
                if (GUILayout.Button("A→B", GUILayout.Width(45)))
                {
                    var differences = CollectComponentDifferences(componentA, componentB);
                    BulkCopyProperties(differences, true);
                }
                if (GUILayout.Button("B→A", GUILayout.Width(45)))
                {
                    var differences = CollectComponentDifferences(componentA, componentB);
                    BulkCopyProperties(differences, false);
                }

                EditorGUILayout.EndHorizontal();

                // Restore indent
                EditorGUI.indentLevel = originalIndent;

                if (componentFoldouts[path][componentKey])
                {
                    EditorGUI.indentLevel++;
                    CompareComponentProperties(componentA, componentB, path, componentKey);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }

            // Bにのみ存在する余分なコンポーネントを警告
            if (listB.Count > listA.Count)
            {
                int extraCount = listB.Count - listA.Count;
                EditorGUILayout.HelpBox($"コンポーネント '{componentTypeName}' がBに{extraCount}個多く存在します", MessageType.Warning);
            }
        }
    }
    
    private void CompareComponentProperties(Component componentA, Component componentB, string path, string componentType)
    {
        SerializedObject serializedA = new SerializedObject(componentA);
        SerializedObject serializedB = new SerializedObject(componentB);
        
        SerializedProperty propA = serializedA.GetIterator();
        bool enterChildren = true;
        
        List<PropertyComparison> comparisons = new List<PropertyComparison>();
        
        while (propA.Next(enterChildren))
        {
            enterChildren = false;
            
            if (propA.name == "m_Script") continue;
            
            // Apply property filter
            if (!string.IsNullOrEmpty(propertyFilter) && 
                !propA.displayName.ToLower().Contains(propertyFilter.ToLower()) &&
                !propA.name.ToLower().Contains(propertyFilter.ToLower()))
                continue;
            
            SerializedProperty propB = serializedB.FindProperty(propA.propertyPath);
            
            if (propB != null)
            {
                bool areEqual = ArePropertiesEqual(propA, propB);
                
                if (!areEqual || showIdenticalValues)
                {
                    comparisons.Add(new PropertyComparison
                    {
                        propertyPath = propA.propertyPath,
                        displayName = propA.displayName,
                        propA = propA.Copy(),
                        propB = propB.Copy(),
                        areEqual = areEqual
                    });
                }
            }
        }
        
        // Display properties with copy buttons
        foreach (var comparison in comparisons)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Difference indicator icon
            if (!comparison.areEqual)
            {
                GUIStyle iconStyle = new GUIStyle(EditorStyles.label);
                iconStyle.normal.textColor = new Color(0.9f, 0.2f, 0.2f);
                iconStyle.fontSize = 12;
                iconStyle.fontStyle = FontStyle.Bold;
                EditorGUILayout.LabelField("▶", iconStyle, GUILayout.Width(15));
            }
            else
            {
                EditorGUILayout.LabelField("", GUILayout.Width(15));
            }
            
            // Property name with color
            GUIStyle labelStyle = comparison.areEqual ? identicalStyle : differenceStyle;
            EditorGUILayout.LabelField(comparison.displayName, labelStyle, GUILayout.Width(150));
            
            // Value A with background color
            Color originalBgColor = GUI.backgroundColor;
            if (!comparison.areEqual)
                GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
            
            GUI.enabled = false;
            EditorGUILayout.PropertyField(comparison.propA, GUIContent.none, GUILayout.Width(200));
            GUI.enabled = true;
            
            GUI.backgroundColor = originalBgColor;
            
            // Copy buttons
            if (GUILayout.Button("A→B", GUILayout.Width(50)))
            {
                CopyPropertyValue(comparison.propA, comparison.propB, serializedB);
            }
            
            if (GUILayout.Button("B→A", GUILayout.Width(50)))
            {
                CopyPropertyValue(comparison.propB, comparison.propA, serializedA);
            }
            
            // Value B with background color
            if (!comparison.areEqual)
                GUI.backgroundColor = new Color(0.7f, 0.7f, 1f);
            
            GUI.enabled = false;
            EditorGUILayout.PropertyField(comparison.propB, GUIContent.none, GUILayout.Width(200));
            GUI.enabled = true;
            
            GUI.backgroundColor = originalBgColor;
            
            EditorGUILayout.EndHorizontal();
        }
    }
    
    private int CountDifferences(Component componentA, Component componentB)
    {
        SerializedObject serializedA = new SerializedObject(componentA);
        SerializedObject serializedB = new SerializedObject(componentB);
        
        SerializedProperty propA = serializedA.GetIterator();
        bool enterChildren = true;
        int differenceCount = 0;
        
        while (propA.Next(enterChildren))
        {
            enterChildren = false;
            
            if (propA.name == "m_Script") continue;
            
            SerializedProperty propB = serializedB.FindProperty(propA.propertyPath);
            
            if (propB != null)
            {
                bool areEqual = ArePropertiesEqual(propA, propB);
                if (!areEqual)
                {
                    differenceCount++;
                }
            }
        }
        
        return differenceCount;
    }
    
    private bool ArePropertiesEqual(SerializedProperty propA, SerializedProperty propB)
    {
        // If ignore object reference option is enabled and both are object references
        if (ignoreObjectReferenceDifferences && 
            propA.propertyType == SerializedPropertyType.ObjectReference &&
            propB.propertyType == SerializedPropertyType.ObjectReference)
        {
            return AreObjectReferencesEquivalent(propA.objectReferenceValue, propB.objectReferenceValue);
        }
        
        // Default comparison
        return SerializedProperty.DataEquals(propA, propB);
    }
    
    private bool AreObjectReferencesEquivalent(Object objA, Object objB)
    {
        // If both are null, they're equal
        if (objA == null && objB == null)
            return true;
        
        // If only one is null, they're different
        if (objA == null || objB == null)
            return false;
        
        // If they're the exact same instance, they're equal
        if (objA == objB)
            return true;
        
        // Compare by name
        if (objA.name != objB.name)
            return false;
        
        // If both are GameObjects or Components, compare their hierarchy paths
        GameObject goA = GetGameObject(objA);
        GameObject goB = GetGameObject(objB);
        
        if (goA != null && goB != null)
        {
            string pathA = GetRelativePathFromRoot(goA.transform);
            string pathB = GetRelativePathFromRoot(goB.transform);
            
            return pathA == pathB;
        }
        
        // For other types, if names match, consider them equivalent
        return true;
    }
    
    private GameObject GetGameObject(Object obj)
    {
        if (obj is GameObject)
            return obj as GameObject;
        
        if (obj is Component)
            return (obj as Component).gameObject;
        
        return null;
    }
    
    private string GetRelativePathFromRoot(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        
        // Build path up to a reasonable root (or max 10 levels)
        int levels = 0;
        while (current != null && levels < 10)
        {
            path = current.name + "/" + path;
            current = current.parent;
            levels++;
        }
        
        return path;
    }
    
    private Object RemapObjectReference(Object sourceReference, Object targetComponent)
    {
        if (sourceReference == null)
            return null;
        
        // Get GameObject from the reference
        GameObject sourceGO = GetGameObject(sourceReference);
        if (sourceGO == null)
        {
            // Not a GameObject or Component, return as-is (e.g. Texture, Material, etc.)
            return sourceReference;
        }
        
        // Get the target GameObject's root
        GameObject targetGO = null;
        if (targetComponent is Component)
        {
            targetGO = (targetComponent as Component).gameObject;
        }
        else if (targetComponent is GameObject)
        {
            targetGO = targetComponent as GameObject;
        }
        
        if (targetGO == null)
            return sourceReference;
        
        // Find the root of both hierarchies
        Transform sourceRoot = FindHierarchyRoot(sourceGO.transform);
        Transform targetRoot = FindHierarchyRoot(targetGO.transform);
        
        if (sourceRoot == null || targetRoot == null)
            return sourceReference;
        
        // Get relative path from source root to source object
        string relativePath = GetRelativePath(sourceRoot, sourceGO.transform);
        
        // Find corresponding object in target hierarchy
        Transform targetTransform = FindTransformByPath(targetRoot, relativePath);
        
        if (targetTransform == null)
        {
            // Path not found, return original reference
            return sourceReference;
        }
        
        // If source was a Component, try to get the same type of component from target
        if (sourceReference is Component)
        {
            Component sourceComp = sourceReference as Component;
            Component targetComp = targetTransform.GetComponent(sourceComp.GetType());
            
            if (targetComp != null)
            {
                return targetComp;
            }
        }
        
        // Return the GameObject if we couldn't find a matching component
        return targetTransform.gameObject;
    }
    
    private Transform FindHierarchyRoot(Transform transform)
    {
        // Find the root by going up the hierarchy
        Transform root = transform;
        while (root.parent != null)
        {
            root = root.parent;
        }
        return root;
    }
    
    private string GetRelativePath(Transform root, Transform target)
    {
        if (root == target)
            return "";
        
        List<string> pathParts = new List<string>();
        Transform current = target;
        
        while (current != null && current != root)
        {
            pathParts.Insert(0, current.name);
            current = current.parent;
        }
        
        if (current == null)
        {
            // Target is not a child of root
            return null;
        }
        
        return string.Join("/", pathParts);
    }
    
    private Transform FindTransformByPath(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return root;
        
        string[] pathParts = path.Split('/');
        Transform current = root;
        
        foreach (string part in pathParts)
        {
            Transform child = current.Find(part);
            if (child == null)
            {
                // Path not found
                return null;
            }
            current = child;
        }
        
        return current;
    }
    
    private void CopyPropertyValue(SerializedProperty source, SerializedProperty target, SerializedObject targetObject)
    {
        Undo.RecordObject(targetObject.targetObject, "Copy Property Value");
        
        // Handle arrays specially
        if (source.isArray && source.propertyType != SerializedPropertyType.String)
        {
            // Adjust target array size to match source
            target.arraySize = source.arraySize;
            targetObject.ApplyModifiedProperties();
            
            // Copy each array element
            for (int i = 0; i < source.arraySize; i++)
            {
                SerializedProperty sourceElement = source.GetArrayElementAtIndex(i);
                SerializedProperty targetElement = target.GetArrayElementAtIndex(i);
                
                CopyPropertyValueRecursive(sourceElement, targetElement, targetObject);
            }
            
            targetObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetObject.targetObject);
            return;
        }
        
        CopyPropertyValueRecursive(source, target, targetObject);
        
        targetObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetObject.targetObject);
    }
    
    private void CopyPropertyValueRecursive(SerializedProperty source, SerializedProperty target, SerializedObject targetObject)
    {
        switch (source.propertyType)
        {
            case SerializedPropertyType.Integer:
                target.intValue = source.intValue;
                break;
            case SerializedPropertyType.Boolean:
                target.boolValue = source.boolValue;
                break;
            case SerializedPropertyType.Float:
                target.floatValue = source.floatValue;
                break;
            case SerializedPropertyType.String:
                target.stringValue = source.stringValue;
                break;
            case SerializedPropertyType.Color:
                target.colorValue = source.colorValue;
                break;
            case SerializedPropertyType.ObjectReference:
                if (remapObjectReferences && source.objectReferenceValue != null)
                {
                    // Try to remap the reference to the target hierarchy
                    Object remappedObject = RemapObjectReference(source.objectReferenceValue, targetObject.targetObject);
                    target.objectReferenceValue = remappedObject;
                }
                else
                {
                    target.objectReferenceValue = source.objectReferenceValue;
                }
                break;
            case SerializedPropertyType.Enum:
                target.enumValueIndex = source.enumValueIndex;
                break;
            case SerializedPropertyType.Vector2:
                target.vector2Value = source.vector2Value;
                break;
            case SerializedPropertyType.Vector3:
                target.vector3Value = source.vector3Value;
                break;
            case SerializedPropertyType.Vector4:
                target.vector4Value = source.vector4Value;
                break;
            case SerializedPropertyType.Quaternion:
                target.quaternionValue = source.quaternionValue;
                break;
            case SerializedPropertyType.Rect:
                target.rectValue = source.rectValue;
                break;
            case SerializedPropertyType.ArraySize:
                target.arraySize = source.arraySize;
                break;
            case SerializedPropertyType.AnimationCurve:
                target.animationCurveValue = source.animationCurveValue;
                break;
            case SerializedPropertyType.Bounds:
                target.boundsValue = source.boundsValue;
                break;
            case SerializedPropertyType.Generic:
                // For generic types, try to copy child properties
                CopyGenericProperty(source, target);
                break;
        }
    }
    
    private void CopyGenericProperty(SerializedProperty source, SerializedProperty target)
    {
        // Check if this is an array (but not a string)
        if (source.isArray && source.propertyType != SerializedPropertyType.String)
        {
            target.arraySize = source.arraySize;
            target.serializedObject.ApplyModifiedProperties();
            
            for (int i = 0; i < source.arraySize; i++)
            {
                SerializedProperty sourceElement = source.GetArrayElementAtIndex(i);
                SerializedProperty targetElement = target.GetArrayElementAtIndex(i);
                CopyPropertyValueRecursive(sourceElement, targetElement, target.serializedObject);
            }
            return;
        }
        
        SerializedProperty sourceChild = source.Copy();
        int sourceDepth = sourceChild.depth;
        bool enterChildren = true;
        
        while (sourceChild.Next(enterChildren) && sourceChild.depth > sourceDepth)
        {
            enterChildren = false;
            
            string relativePath = sourceChild.propertyPath.Substring(source.propertyPath.Length + 1);
            SerializedProperty targetProp = target.FindPropertyRelative(relativePath);
            
            if (targetProp != null)
            {
                // Handle arrays within generic properties
                if (sourceChild.isArray && sourceChild.propertyType != SerializedPropertyType.String)
                {
                    targetProp.arraySize = sourceChild.arraySize;
                    target.serializedObject.ApplyModifiedProperties();
                    
                    for (int i = 0; i < sourceChild.arraySize; i++)
                    {
                        SerializedProperty sourceElement = sourceChild.GetArrayElementAtIndex(i);
                        SerializedProperty targetElement = targetProp.GetArrayElementAtIndex(i);
                        CopyPropertyValueRecursive(sourceElement, targetElement, target.serializedObject);
                    }
                    
                    // Skip the array elements in the iteration
                    enterChildren = false;
                }
                else if (sourceChild.propertyType != SerializedPropertyType.Generic)
                {
                    // Special handling for object references in generic properties
                    if (sourceChild.propertyType == SerializedPropertyType.ObjectReference && 
                        remapObjectReferences && 
                        sourceChild.objectReferenceValue != null)
                    {
                        Object remappedObject = RemapObjectReference(sourceChild.objectReferenceValue, target.serializedObject.targetObject);
                        targetProp.objectReferenceValue = remappedObject;
                    }
                    else
                    {
                        CopyPropertyValueRecursive(sourceChild, targetProp, target.serializedObject);
                    }
                }
                else
                {
                    // For nested generic properties, recurse
                    enterChildren = true;
                }
            }
        }
    }
    
    private class PropertyComparison
    {
        public string propertyPath;
        public string displayName;
        public SerializedProperty propA;
        public SerializedProperty propB;
        public bool areEqual;
    }

    // 一括コピー用の差分情報
    private class PropertyDifference
    {
        public Component componentA;
        public Component componentB;
        public string propertyPath;
    }

    // 全体の差分を収集
    private List<PropertyDifference> CollectAllDifferences()
    {
        var differences = new List<PropertyDifference>();
        if (objectA == null || objectB == null) return differences;

        CollectHierarchyDifferencesRecursive(objectA.transform, objectB.transform, differences);
        return differences;
    }

    // 階層を再帰的に走査して差分を収集
    private void CollectHierarchyDifferencesRecursive(Transform transformA, Transform transformB, List<PropertyDifference> differences)
    {
        // hideIdenticalHierarchiesの設定を考慮
        if (hideIdenticalHierarchies && !HasDifferencesInHierarchy(transformA, transformB))
            return;

        // このノードのコンポーネント差分を収集
        CollectNodeDifferences(transformA.gameObject, transformB.gameObject, differences);

        // 子階層を再帰的に処理
        Dictionary<string, Transform> childrenA = new Dictionary<string, Transform>();
        Dictionary<string, Transform> childrenB = new Dictionary<string, Transform>();

        for (int i = 0; i < transformA.childCount; i++)
        {
            Transform child = transformA.GetChild(i);
            childrenA[child.name] = child;
        }

        for (int i = 0; i < transformB.childCount; i++)
        {
            Transform child = transformB.GetChild(i);
            childrenB[child.name] = child;
        }

        foreach (var kvp in childrenA)
        {
            if (childrenB.ContainsKey(kvp.Key))
            {
                CollectHierarchyDifferencesRecursive(kvp.Value, childrenB[kvp.Key], differences);
            }
        }
    }

    // 単一階層ノードの差分を収集
    private List<PropertyDifference> CollectNodeDifferences(GameObject objA, GameObject objB)
    {
        var differences = new List<PropertyDifference>();
        CollectNodeDifferences(objA, objB, differences);
        return differences;
    }

    private void CollectNodeDifferences(GameObject objA, GameObject objB, List<PropertyDifference> differences)
    {
        Component[] componentsA = objA.GetComponents<Component>();
        Component[] componentsB = objB.GetComponents<Component>();

        // 同じ型のコンポーネントをインデックスでマッチングするためにグループ化
        var componentsByTypeA = new Dictionary<System.Type, List<Component>>();
        var componentsByTypeB = new Dictionary<System.Type, List<Component>>();

        foreach (var comp in componentsA)
        {
            if (comp == null) continue;
            var type = comp.GetType();
            if (!componentsByTypeA.ContainsKey(type))
                componentsByTypeA[type] = new List<Component>();
            componentsByTypeA[type].Add(comp);
        }

        foreach (var comp in componentsB)
        {
            if (comp == null) continue;
            var type = comp.GetType();
            if (!componentsByTypeB.ContainsKey(type))
                componentsByTypeB[type] = new List<Component>();
            componentsByTypeB[type].Add(comp);
        }

        foreach (var kvp in componentsByTypeA)
        {
            System.Type componentTypeKey = kvp.Key;
            List<Component> listA = kvp.Value;
            string componentTypeName = componentTypeKey.Name;

            // コンポーネントフィルター適用
            if (!string.IsNullOrEmpty(componentFilter) &&
                !componentTypeName.ToLower().Contains(componentFilter.ToLower()))
                continue;

            // Bに同じ型のコンポーネントがない場合はスキップ
            if (!componentsByTypeB.ContainsKey(componentTypeKey))
                continue;

            List<Component> listB = componentsByTypeB[componentTypeKey];
            int count = System.Math.Min(listA.Count, listB.Count);

            // インデックスでマッチング
            for (int i = 0; i < count; i++)
            {
                CollectComponentDifferences(listA[i], listB[i], differences);
            }
        }
    }

    // 単一コンポーネントの差分を収集
    private List<PropertyDifference> CollectComponentDifferences(Component componentA, Component componentB)
    {
        var differences = new List<PropertyDifference>();
        CollectComponentDifferences(componentA, componentB, differences);
        return differences;
    }

    private void CollectComponentDifferences(Component componentA, Component componentB, List<PropertyDifference> differences)
    {
        SerializedObject serializedA = new SerializedObject(componentA);
        SerializedObject serializedB = new SerializedObject(componentB);

        SerializedProperty propA = serializedA.GetIterator();
        bool enterChildren = true;

        while (propA.Next(enterChildren))
        {
            enterChildren = false;

            if (propA.name == "m_Script") continue;

            // プロパティフィルター適用
            if (!string.IsNullOrEmpty(propertyFilter) &&
                !propA.displayName.ToLower().Contains(propertyFilter.ToLower()) &&
                !propA.name.ToLower().Contains(propertyFilter.ToLower()))
                continue;

            SerializedProperty propB = serializedB.FindProperty(propA.propertyPath);

            if (propB != null)
            {
                bool areEqual = ArePropertiesEqual(propA, propB);

                // 差分があるプロパティのみ収集
                if (!areEqual)
                {
                    differences.Add(new PropertyDifference
                    {
                        componentA = componentA,
                        componentB = componentB,
                        propertyPath = propA.propertyPath
                    });
                }
            }
        }
    }

    // 一括コピー実行
    private void BulkCopyProperties(List<PropertyDifference> differences, bool aToB)
    {
        if (differences.Count == 0) return;

        string direction = aToB ? "A→B" : "B→A";
        string message = $"{differences.Count}件の差分を{direction}にコピーしますか？";

        if (!EditorUtility.DisplayDialog("一括コピー確認", message, "コピー", "キャンセル"))
            return;

        Undo.SetCurrentGroupName($"Bulk Copy Properties {direction}");
        int undoGroup = Undo.GetCurrentGroup();

        // コピー先のオブジェクトをUndo登録
        var targetObjects = differences
            .Select(d => aToB ? d.componentB : d.componentA)
            .Distinct()
            .Cast<Object>()
            .ToArray();

        foreach (var obj in targetObjects)
        {
            Undo.RecordObject(obj, $"Bulk Copy {direction}");
        }

        // 各差分をコピー
        foreach (var diff in differences)
        {
            Component source = aToB ? diff.componentA : diff.componentB;
            Component target = aToB ? diff.componentB : diff.componentA;

            SerializedObject serializedSource = new SerializedObject(source);
            SerializedObject serializedTarget = new SerializedObject(target);

            SerializedProperty sourceProp = serializedSource.FindProperty(diff.propertyPath);
            SerializedProperty targetProp = serializedTarget.FindProperty(diff.propertyPath);

            if (sourceProp != null && targetProp != null)
            {
                CopyPropertyValueRecursive(sourceProp, targetProp, serializedTarget);
                serializedTarget.ApplyModifiedProperties();
            }
        }

        // 変更をマーク
        foreach (var obj in targetObjects)
        {
            EditorUtility.SetDirty(obj);
        }

        Undo.CollapseUndoOperations(undoGroup);
    }
}