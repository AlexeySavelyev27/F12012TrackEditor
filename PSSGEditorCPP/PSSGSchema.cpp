#include "PSSGSchema.h"
#include "PSSGNode.h"

void PSSGSchema::BuildFromTree(const PSSGNode& root) {
    std::vector<const PSSGNode*> stack;
    stack.push_back(&root);
    std::vector<std::string> node_names;
    std::unordered_map<std::string, std::vector<std::string>> attr_map;
    while(!stack.empty()) {
        const PSSGNode* n = stack.back();
        stack.pop_back();
        if(std::find(node_names.begin(), node_names.end(), n->name) == node_names.end()) {
            node_names.push_back(n->name);
        }
        auto& vec = attr_map[n->name];
        for(const auto& kv : n->attributes) {
            if(std::find(vec.begin(), vec.end(), kv.first) == vec.end()) {
                vec.push_back(kv.first);
            }
        }
        for(const auto& c : n->children) {
            stack.push_back(&c);
        }
    }
    for(size_t i=0;i<node_names.size();++i) {
        uint32_t id = static_cast<uint32_t>(i+1);
        const std::string& name = node_names[i];
        node_id_to_name[id] = name;
        node_name_to_id[name] = id;
    }
    for(const auto& nm : node_names) {
        uint32_t node_id = node_name_to_id[nm];
        const auto& attrs = attr_map[nm];
        auto& id_map = attr_id_to_name[node_id];
        auto& name_map = attr_name_to_id[nm];
        for(size_t i=0;i<attrs.size();++i) {
            uint32_t aid = static_cast<uint32_t>(i+1);
            id_map[aid] = attrs[i];
            name_map[attrs[i]] = aid;
        }
    }
}
